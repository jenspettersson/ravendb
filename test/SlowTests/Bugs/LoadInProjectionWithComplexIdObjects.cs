using System.Linq;
using FastTests;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Queries;
using Xunit;


namespace SlowTests.Bugs
{
    public class LoadInProjectionWithComplexIdObjects : RavenTestBase
    {
        public class IdReference
        {
            public string Id { get; }

            public IdReference(string id)
            {
                Id = id;
            }
        }
        
        public class Package
        {
            public string Id { get; set; }
            
            public string ReferenceId { get; set; }
            public IdReference ComplexRuleReference { get; set; }

            public string SomethingElseReference {get; set; }
            public IdReference ComplexSomethingElseReference { get; set; }
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
        public void Projection_with_load_on_complex_object_id_last()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Package
                    {
                        SomethingElseReference = "somethingElse/123-A",
                        ComplexRuleReference = new IdReference("rules/1")
                    });
                    session.Store(new Rule
                    {
                        Id = "rules/1",
                        Name = "Test rule"
                    });
                    session.Store(new SomethingElse
                    {
                        Id = "somethingElse/123-A",
                        Name = "Something else name"
                    });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Package>();

                    var projection = from package in query
                        let somethingElse = RavenQuery.Load<SomethingElse>(package.SomethingElseReference) 
                        let rule = RavenQuery.Load<Rule>(package.ComplexRuleReference.Id)
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
        public void Projection_with_load_on_complex_object_id_first()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Package
                    {
                        SomethingElseReference = "somethingElse/123-A",
                        ComplexRuleReference = new IdReference("rules/1")
                    });
                    session.Store(new Rule
                    {
                        Id = "rules/1",
                        Name = "Test rule"
                    });
                    session.Store(new SomethingElse
                    {
                        Id = "somethingElse/123-A",
                        Name = "Something else name"
                    });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Package>();

                    var projection = from package in query
                        let rule = RavenQuery.Load<Rule>(package.ComplexRuleReference.Id)
                        let somethingElse = RavenQuery.Load<SomethingElse>(package.SomethingElseReference) 
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
        public void Projection_with_load_using_only_complex_object_ids()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Package
                    {
                        ComplexSomethingElseReference = new IdReference("somethingElse/123-A"),
                        ComplexRuleReference = new IdReference("rules/1")
                    });
                    session.Store(new Rule
                    {
                        Id = "rules/1",
                        Name = "Test rule"
                    });
                    session.Store(new SomethingElse
                    {
                        Id = "somethingElse/123-A",
                        Name = "Something else name"
                    });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Package>();

                    var projection = from package in query
                        let rule = RavenQuery.Load<Rule>(package.ComplexRuleReference.Id)
                        let somethingElse = RavenQuery.Load<SomethingElse>(package.ComplexSomethingElseReference.Id) 
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

        //This just illustrates that the order doesn't matter if you don't use complex objets as reference ids
        [Fact]
        public void Projection_with_load_using_only_simple_strings()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Package
                    {
                        SomethingElseReference = "somethingElse/123-A",
                        ReferenceId = "rules/1"
                    });
                    session.Store(new Rule
                    {
                        Id = "rules/1",
                        Name = "Test rule"
                    });
                    session.Store(new SomethingElse
                    {
                        Id = "somethingElse/123-A",
                        Name = "Something else name"
                    });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Package>();

                    //Same order as the first failing test but this works as it uses a simple string for the reference id load
                    var projection = from package in query
                        let rule = RavenQuery.Load<Rule>(package.ReferenceId) 
                        let somethingElse = RavenQuery.Load<SomethingElse>(package.SomethingElseReference) 
                        
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
