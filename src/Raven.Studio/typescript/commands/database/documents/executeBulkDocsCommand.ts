import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class executeBulkDocsCommand extends commandBase {
    constructor(public docs: Raven.Server.Documents.Handlers.CommandData[], private db: database) {
        super();
    }

    execute(): JQueryPromise<Raven.Server.Documents.Handlers.CommandData[]> {
        return this.post(endpoints.databases.batch.bulk_docs, JSON.stringify(this.docs), this.db);
    }
}

export = executeBulkDocsCommand; 
