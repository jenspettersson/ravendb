using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Database.Commercial;
using Raven.Database.Config;
using Raven.Database.Extensions;
using Raven.Database.Server.Connections;
using Raven.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Raven.Abstractions.Exceptions;
using Raven.Database.Server.Security;

namespace Raven.Database.Server.Tenancy
{
    public class DatabasesLandlord : AbstractLandlord<DocumentDatabase>
    {
        public event Action<RavenConfiguration> SetupTenantConfiguration = delegate { };

        public event Action<string> OnDatabaseLoaded = delegate { };

        private bool initialized;
        private const string DATABASES_PREFIX = "Raven/Databases/";
        public override string ResourcePrefix { get { return DATABASES_PREFIX; } }

        public TimeSpan MaxIdleTimeForTenantDatabase { get; private set; }
        
        public TimeSpan FrequencyToCheckForIdleDatabases { get; private set; }

        public DatabasesLandlord(DocumentDatabase systemDatabase) : base(systemDatabase)
        {
            MaxIdleTimeForTenantDatabase = SystemConfiguration.Tenants.MaxIdleTime.AsTimeSpan;

            FrequencyToCheckForIdleDatabases = systemConfiguration.Tenants.FrequencyToCheckForIdle.AsTimeSpan;

            string tempPath = SystemConfiguration.Core.TempPath;
            var fullTempPath = tempPath + Constants.TempUploadsDirectoryName;
            if (File.Exists(fullTempPath))
            {
                try
                {
                    File.Delete(fullTempPath);
                }
                catch (Exception)
                {
                    // we ignore this issue, nothing to do now, and we'll only see
                    // this as an error if there are actually uploads
                }
            }
            if (Directory.Exists(fullTempPath))
            {
                try
                {
                    Directory.Delete(fullTempPath, true);
                }
                catch (Exception)
                {
                    // there is nothing that we can do here, and it is possible that we have
                    // another database doing uploads for the same user, so we'll just 
                    // not any cleanup. Worst case, we'll waste some memory.
                }
            }

            Init();
        }

        public DocumentDatabase SystemDatabase
        {
            get { return systemDatabase; }
        }

        public RavenConfiguration SystemConfiguration
        {
            get { return systemConfiguration; }
        }

        public RavenConfiguration CreateTenantConfiguration(string tenantId, bool ignoreDisabledDatabase = false)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Equals("<system>", StringComparison.OrdinalIgnoreCase))
                return systemConfiguration;
            var document = GetTenantDatabaseDocument(tenantId, ignoreDisabledDatabase);
            if (document == null)
                return null;

            return CreateConfiguration(tenantId, document, RavenConfiguration.GetKey(x => x.Core.DataDirectory), systemConfiguration);
        }

        private DatabaseDocument GetTenantDatabaseDocument(string tenantId, bool ignoreDisabledDatabase = false)
        {
            JsonDocument jsonDocument;
            using (systemDatabase.DisableAllTriggersForCurrentThread())
                jsonDocument = systemDatabase.Documents.Get(Constants.Database.Prefix + tenantId);
            if (jsonDocument == null ||
                jsonDocument.Metadata == null ||
                jsonDocument.Metadata.Value<bool>(Constants.RavenDocumentDoesNotExists) ||
                jsonDocument.Metadata.Value<bool>(Constants.RavenDeleteMarker))
                return null;

            var document = jsonDocument.DataAsJson.JsonDeserialization<DatabaseDocument>();
            if (document.Settings[RavenConfiguration.GetKey(x => x.Core.DataDirectory)] == null)
                throw new InvalidOperationException("Could not find " + RavenConfiguration.GetKey(x => x.Core.DataDirectory));

            if (document.Disabled && !ignoreDisabledDatabase)
                throw new InvalidOperationException("The database has been disabled.");

            return document;
        }

        public override async Task<DocumentDatabase> GetResourceInternal(string resourceName)
        {
            if (string.Equals("<system>", resourceName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(resourceName))
                return systemDatabase;

            Task<DocumentDatabase> db;
            if (TryGetOrCreateResourceStore(resourceName, out db))
                return await db.ConfigureAwait(false);
            return null;
        }

        public override bool TryGetOrCreateResourceStore(string tenantId, out Task<DocumentDatabase> database)
        {
            if (Locks.Contains(DisposingLock))
                throw new ObjectDisposedException("DatabaseLandlord", "Server is shutting down, can't access any databases");

            if (Locks.Contains(tenantId))
                throw new InvalidOperationException("Database '" + tenantId + "' is currently locked and cannot be accessed.");

            ManualResetEvent cleanupLock;
            if (Cleanups.TryGetValue(tenantId, out cleanupLock) && cleanupLock.WaitOne(MaxTimeForTaskToWaitForDatabaseToLoad) == false)
                throw new InvalidOperationException($"Database '{tenantId}' is currently being restarted and cannot be accessed. We already waited {MaxTimeForTaskToWaitForDatabaseToLoad.TotalSeconds} seconds.");

            if (ResourcesStoresCache.TryGetValue(tenantId, out database))
            {
                if (database.IsFaulted || database.IsCanceled)
                {
                    ResourcesStoresCache.TryRemove(tenantId, out database);
                    DateTime time;
                    LastRecentlyUsed.TryRemove(tenantId, out time);
                    // and now we will try creating it again
                }
                else
                {
                    return true;
                }
            }

            var config = CreateTenantConfiguration(tenantId);
            if (config == null)
                return false;

            var hasAcquired = false;
            try
            {
                if (!ResourceSemaphore.Wait(ConcurrentResourceLoadTimeout))
                    throw new ConcurrentLoadTimeoutException("Too much counters loading concurrently, timed out waiting for them to load.");

                hasAcquired = true;
                database = ResourcesStoresCache.GetOrAdd(tenantId, __ => Task.Factory.StartNew(() =>
                {
                    var transportState = ResourseTransportStates.GetOrAdd(tenantId, s => new TransportState());

                    AssertLicenseParameters(config);
                    var documentDatabase = new DocumentDatabase(config, systemDatabase, transportState);

                    documentDatabase.SpinBackgroundWorkers(false);
                    documentDatabase.Disposing += DocumentDatabaseDisposingStarted;
                    documentDatabase.DisposingEnded += DocumentDatabaseDisposingEnded;
                    documentDatabase.StorageInaccessible += UnloadDatabaseOnStorageInaccessible;
                    // register only DB that has incremental backup set.
                    documentDatabase.OnBackupComplete += OnDatabaseBackupCompleted;

                    // if we have a very long init process, make sure that we reset the last idle time for this db.
                    LastRecentlyUsed.AddOrUpdate(tenantId, SystemTime.UtcNow, (_, time) => SystemTime.UtcNow);
                    documentDatabase.RequestManager = SystemDatabase.RequestManager;
                    return documentDatabase;
                }).ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                        OnDatabaseLoaded(tenantId);

                    if (task.Status == TaskStatus.Faulted) // this observes the task exception
                    {
                        Logger.WarnException("Failed to create database " + tenantId, task.Exception);
                    }
                    return task;
                }).Unwrap());

                if (database.IsFaulted && database.Exception != null)
                {
                    // if we are here, there is an error, and if there is an error, we need to clear it from the 
                    // resource store cache so we can try to reload it.
                    // Note that we return the faulted task anyway, because we need the user to look at the error
                    if (database.Exception.Data.Contains("Raven/KeepInResourceStore") == false)
                    {
                        Task<DocumentDatabase> val;
                        ResourcesStoresCache.TryRemove(tenantId, out val);
                    }
                }

                return true;
            }
            finally
            {
                if (hasAcquired)
                    ResourceSemaphore.Release();
            }
        }

        protected RavenConfiguration CreateConfiguration(
                        string tenantId,
                        DatabaseDocument document,
                        string folderPropName,
                        RavenConfiguration parentConfiguration)
        {
            var config = RavenConfiguration.CreateFrom(parentConfiguration);

            if (config.GetSetting(RavenConfiguration.GetKey(x => x.Core.CompiledIndexCacheDirectory)) == null)
            {
                var compiledIndexCacheDirectory = parentConfiguration.Core.CompiledIndexCacheDirectory;
                config.SetSetting(RavenConfiguration.GetKey(x => x.Core.CompiledIndexCacheDirectory), compiledIndexCacheDirectory);  
            }

            if (config.GetSetting(RavenConfiguration.GetKey(x => x.Core.TempPath)) == null)
                config.SetSetting(RavenConfiguration.GetKey(x => x.Core.TempPath), parentConfiguration.Core.TempPath);  

            SetupTenantConfiguration(config);

            config.CustomizeValuesForDatabaseTenant(tenantId);

            foreach (var setting in document.Settings)
            {
                config.SetSetting(setting.Key, setting.Value);
            }
            Unprotect(document);

            foreach (var securedSetting in document.SecuredSettings)
            {
                config.SetSetting(securedSetting.Key, securedSetting.Value);
            }

            config.SetSetting(folderPropName, config.GetSetting(folderPropName).ToFullPath(parentConfiguration.Core.DataDirectory));
            config.SetSetting(RavenConfiguration.GetKey(x => x.Storage.JournalsStoragePath), config.GetSetting(RavenConfiguration.GetKey(x => x.Storage.JournalsStoragePath)).ToFullPath(parentConfiguration.Core.DataDirectory));

            config.DatabaseName = tenantId;
            config.IsTenantDatabase = true;

            config.Initialize();
            config.CopyParentSettings(parentConfiguration);
            return config;
        }

        public void Protect(DatabaseDocument databaseDocument)
        {
            if (databaseDocument.SecuredSettings == null)
            {
                databaseDocument.SecuredSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            foreach (var prop in databaseDocument.SecuredSettings.ToList())
            {
                if (prop.Value == null)
                    continue;
                var bytes = Encoding.UTF8.GetBytes(prop.Value);
                var entrophy = Encoding.UTF8.GetBytes(prop.Key);
                var protectedValue = ProtectedData.Protect(bytes, entrophy, DataProtectionScope.CurrentUser);
                databaseDocument.SecuredSettings[prop.Key] = Convert.ToBase64String(protectedValue);
            }
        }

        private void OnDatabaseBackupCompleted(DocumentDatabase db)
        {
            var dbStatusKey = "Raven/BackupStatus/" + db.Name;
            var statusDocument = db.Documents.Get(dbStatusKey);
            DatabaseOperationsStatus status;
            if (statusDocument == null)
            {
                status = new DatabaseOperationsStatus();
            }
            else
            {
                status = statusDocument.DataAsJson.JsonDeserialization<DatabaseOperationsStatus>();
            }
            status.LastBackup = SystemTime.UtcNow;
            var json = RavenJObject.FromObject(status);
            json.Remove("Id");
            systemDatabase.Documents.Put(dbStatusKey, null, json, new RavenJObject(), null);
        }

        private void AssertLicenseParameters(RavenConfiguration config)
        {
            string maxDatabases;
            if (ValidateLicense.CurrentLicense.Attributes.TryGetValue("numberOfDatabases", out maxDatabases))
            {
                if (string.Equals(maxDatabases, "unlimited", StringComparison.OrdinalIgnoreCase) == false)
                {
                    var numberOfAllowedDbs = int.Parse(maxDatabases);

                    int nextPageStart = 0;
                    var databases = systemDatabase.Documents.GetDocumentsWithIdStartingWith("Raven/Databases/", null, null, 0, numberOfAllowedDbs, CancellationToken.None, ref nextPageStart).ToList();
                    if (databases.Count > numberOfAllowedDbs)
                        throw new InvalidOperationException(
                            "You have reached the maximum number of databases that you can have according to your license: " + 
                            numberOfAllowedDbs + Environment.NewLine +
                            "But we detect: " + databases.Count + " databases" + Environment.NewLine +
                            "You can either upgrade your RavenDB license or delete a database from the server");
                }
            }

            Authentication.AssertLicensedBundles(config.Core.ActiveBundles);
                }

        public void ForAllDatabases(Action<DocumentDatabase> action, bool excludeSystemDatabase = false)
        {
            if (!excludeSystemDatabase) action(systemDatabase);
            foreach (var value in ResourcesStoresCache
                .Select(db => db.Value)
                .Where(value => value.Status == TaskStatus.RanToCompletion))
            {
                action(value.Result);
            }
        }

        protected override DateTime LastWork(DocumentDatabase resource)
        {
            var databaseSizeInformation = resource.TransactionalStorage.GetDatabaseSize();
            return resource.WorkContext.LastWorkTime +
                   // this allow us to increase the time large databases will be held in memory
                   // because they are more expensive to unload & reload. Using this method, we'll
                   // add 0.5 ms per each KB, or roughly half a second of idle time per MB.
                   // A DB with 1GB will remain live another 16 minutes after being item. Given the default idle time
                   // that means that we'll keep it alive for about 30 minutes without shutting down.
                   // A database with 50GB will take roughly 8 hours of idle time to shut down. 
                   TimeSpan.FromMilliseconds(databaseSizeInformation.AllocatedSizeInBytes / 1024L / 2);
        }

        public void Init()
        {
            if (initialized)
                return;
            initialized = true;
            SystemDatabase.Notifications.OnDocumentChange += (database, notification, doc) =>
            {
                if (notification.Id == null)
                    return;
                const string ravenDbPrefix = "Raven/Databases/";
                if (notification.Id.StartsWith(ravenDbPrefix, StringComparison.InvariantCultureIgnoreCase) == false)
                    return;
                var dbName = notification.Id.Substring(ravenDbPrefix.Length);
                Logger.Info("Shutting down database {0} because the tenant database has been updated or removed", dbName);
                Cleanup(dbName, skipIfActiveInDuration: null, notificationType: notification.Type);
            };
        }

        public bool IsDatabaseLoaded(string tenantName)
        {
            if (tenantName == Constants.SystemDatabase)
                return true;

            Task<DocumentDatabase> dbTask;
            if (ResourcesStoresCache.TryGetValue(tenantName, out dbTask) == false)
                return false;

            return dbTask != null && dbTask.Status == TaskStatus.RanToCompletion;
        }

        private void DocumentDatabaseDisposingStarted(object documentDatabase, EventArgs args)
        {
            try
            {
                var database = documentDatabase as DocumentDatabase;
                if (database == null)
                {
                    return;
                }

                ResourcesStoresCache.Set(database.Name, (dbName) =>
                {
                    var tcs = new TaskCompletionSource<DocumentDatabase>();
                    tcs.SetException(new ObjectDisposedException(database.Name, "Database named " + database.Name + " is being disposed right now and cannot be accessed.\r\n" +
                                                                 "Access will be available when the dispose process will end")
                    {
                        Data =
                        {
                            {"Raven/KeepInResourceStore", "true"}
                        }
                    });
                    // we need to observe this task exception in case no one is actually looking at it during disposal
                    GC.KeepAlive(tcs.Task.Exception);
                    return tcs.Task;
                });
            }
            catch (Exception ex)
            {
                Logger.WarnException("Failed to substitute database task with temporary place holder. This should not happen", ex);
            }
        }

        private void DocumentDatabaseDisposingEnded(object documentDatabase, EventArgs args)
        {
            try
            {
                var database = documentDatabase as DocumentDatabase;
                if (database == null)
                {
                    return;
                }

                ResourcesStoresCache.Remove(database.Name);
            }
            catch (Exception ex)
            {
                Logger.ErrorException("Failed to remove database at the end of the disposal. This should not happen", ex);
            }
        }

        private void UnloadDatabaseOnStorageInaccessible(object documentDatabase, EventArgs eventArgs)
        {
            try
            {
                var database = documentDatabase as DocumentDatabase;
                if (database == null)
                {
                    return;
                }

                Task.Run(() =>
                {
                    Thread.Sleep(2000); // let the exception thrown by the storage to be propagated into the client

                    Logger.Warn("Shutting down database {0} because its storage has become inaccessible", database.Name);

                    Cleanup(database.Name, skipIfActiveInDuration: null, shouldSkip: x => false);
                });

            }
            catch (Exception ex)
            {
                Logger.ErrorException("Failed to cleanup database that storage is inaccessible. This should not happen", ex);
            }

        }
    }
}
