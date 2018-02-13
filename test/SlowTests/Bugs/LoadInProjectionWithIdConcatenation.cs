using System.Linq;
using FastTests;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Queries;
using Xunit;


namespace SlowTests.Bugs
{
    public class LoadInProjectionWithIdConcatenation : RavenTestBase
    {
        public class RuleReference
        {
            public string Id { get; }

            public RuleReference(string id)
            {
                Id = id;
            }
        }
        
        public class Package
        {
            public string Id { get; set; }
            public string ReferenceId { get; set; }
            public RuleReference Reference { get; set; }
            public string SomethingElseShortReference {get; set; }
        }

        public class Rule
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }

        public class SomethingElse
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }

        [Fact]
        public void Projection_with_load_on_complex_object_with_string_concatenation_last()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Package
                    {
                        SomethingElseShortReference = "123-A",
                        Reference = new RuleReference("rules/1")
                    });
                    session.Store(new Rule
                    {
                        Id = "rules/1",
                        Name = "Test rule"
                    });
                    session.Store(new SomethingElse
                    {
                        Id = "SomethingElse/123-A",
                        Name = "Something else name"
                    });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Package>();

                    var projection = from package in query
                        let rule = RavenQuery.Load<Rule>(package.Reference.Id) //This Load works as the concatination load occurs after
                        let somethingElse = RavenQuery.Load<SomethingElse>("SomethingElse/" + package.SomethingElseShortReference) 
                        select new
                        {
                            PackageId = package.Id,
                            RuleName = rule.Name,
                            SomethingElseName = somethingElse.Name
                        };

                    var result = projection.First();

                    Assert.Equal("Test rule", result.RuleName);
                    Assert.Equal("Something else name", result.SomethingElseName);
                }
            }
        }

        [Fact]
        public void Projection_with_load_on_complex_object_with_string_concatenation_first()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Package
                    {
                        SomethingElseShortReference = "123-A",
                        Reference = new RuleReference("rules/1")
                    });
                    session.Store(new Rule
                    {
                        Id = "rules/1",
                        Name = "Test rule"
                    });
                    session.Store(new SomethingElse
                    {
                        Id = "SomethingElse/123-A",
                        Name = "Something else name"
                    });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Package>();

                    var projection = from package in query
                        let somethingElse = RavenQuery.Load<SomethingElse>("SomethingElse/" + package.SomethingElseShortReference) 
                        let rule = RavenQuery.Load<Rule>(package.Reference.Id) //This Load causes an exception if it occurs after string concatination load
                        select new
                        {
                            PackageId = package.Id,
                            RuleName = rule.Name,
                            SomethingElseName = somethingElse.Name
                        };

                    var result = projection.First();

                    Assert.Equal("Test rule", result.RuleName);
                    Assert.Equal("Something else name", result.SomethingElseName);
                }
            }
        }

        [Fact]
        public void Projection_with_load_on_simple_string_with_string_concatenation_first()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Package
                    {
                        SomethingElseShortReference = "123-A",
                        ReferenceId = "rules/1"
                    });
                    session.Store(new Rule
                    {
                        Id = "rules/1",
                        Name = "Test rule"
                    });
                    session.Store(new SomethingElse
                    {
                        Id = "SomethingElse/123-A",
                        Name = "Something else name"
                    });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Package>();

                    var projection = from package in query
                        let somethingElse = RavenQuery.Load<SomethingElse>("SomethingElse/" + package.SomethingElseShortReference) 
                        let rule = RavenQuery.Load<Rule>(package.ReferenceId) //This Load works as the ReferenceId is a simple string
                        select new
                        {
                            PackageId = package.Id,
                            RuleName = rule.Name,
                            SomethingElseName = somethingElse.Name
                        };

                    var result = projection.First();

                    Assert.Equal("Test rule", result.RuleName);
                    Assert.Equal("Something else name", result.SomethingElseName);
                }
            }
        }
    }
}
