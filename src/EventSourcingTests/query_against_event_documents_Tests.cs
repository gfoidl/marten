using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

public class query_against_event_documents_Tests: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;
    private readonly MembersJoined joined1 = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
    private readonly MembersDeparted departed1 = new MembersDeparted { Members = new[] { "Thom" } };

    private readonly MembersJoined joined2 = new MembersJoined { Members = new string[] { "Nynaeve", "Egwene" } };
    private readonly MembersDeparted departed2 = new MembersDeparted { Members = new[] { "Matt" } };

    #region sample_query-against-event-data
    [Fact]
    public void can_query_against_event_type()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        theSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    #endregion

    [Fact]
    public void can_query_against_event_type_with_camel_casing()
    {
        StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        theSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public async Task can_query_against_event_metadata()
    {
        var sql = theSession.Events
            .QueryAllRawEvents()
            .Where(x => x.Sequence >= 123)
            .Where(x => x.EventTypeName == "SomethingHappenedEvent")
            .Where(x => x.DotNetTypeName == "AlsoWrong")
            .OrderBy(x => x.EventTypeName)
            .Take(3)
            .ToCommand().CommandText;

        sql.ShouldNotContain("d.data ->> 'EventTypeName' = :p1", StringComparisonOption.Default);
        sql.ShouldNotContain("d.data ->> 'DotNetTypeName' = :p2", StringComparisonOption.Default);
    }

    [Fact]
    public void can_query_against_event_type_with_snake_casing()
    {
        StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        theSession.Events.QueryRawEventDataOnly<MembersDeparted>().Where(x => x.Members.Contains("Matt"))
            .Single().Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public void can_project_event_id_guid()
    {
        var id = Guid.NewGuid();
        theSession.Events.Append(id, new QuestEnded { Id = id });

        theSession.SaveChanges();

        theSession.Events.QueryRawEventDataOnly<QuestEnded>().Select(e => e.Id).ToList()
            .ShouldHaveTheSameElementsAs(id);
    }

    [Fact]
    public void can_project_event_id_string()
    {
        StoreOptions(_ => _.Events.StreamIdentity = Marten.Events.StreamIdentity.AsString);

        var id = "foo";
        theSession.Events.Append(id, new StringIdEvent(id));

        theSession.SaveChanges();

        theSession.Events.QueryRawEventDataOnly<StringIdEvent>().Select(e => e.Id).ToList()
            .ShouldHaveTheSameElementsAs(id);
    }

    [Fact]
    public void will_not_blow_up_if_searching_for_events_before_event_store_is_warmed_up()
    {
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Any().ShouldBeFalse();
    }


    [Fact]
    public void can_query_against_event_type_with_different_schema_name_with_camel_casing()
    {
        StoreOptions(_ =>
        {
            _.Events.DatabaseSchemaName = SchemaName + "_events";

            _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);

            _.Events.AddEventType(typeof(MembersDeparted));
        });

        theStore.Advanced.Clean.DeleteAllEventData();


        theStore.StorageFeatures.FindMapping(typeof(MembersDeparted))
            .TableName.Schema.ShouldBe("query_against_event_documents_tests_events");

        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        theSession.Events.QueryRawEventDataOnly<MembersJoined>().Count().ShouldBe(2);
        theSession.Events.QueryRawEventDataOnly<MembersJoined>().ToArray().SelectMany(x => x.Members).Distinct()
            .OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("Egwene", "Matt", "Nynaeve", "Perrin", "Rand", "Thom");

        theSession.Events.QueryRawEventDataOnly<MembersDeparted>()
            .Single(x => x.Members.Contains("Matt")).Id.ShouldBe(departed2.Id);
    }

    [Fact]
    public void can_fetch_all_events()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        var results = theSession.Events.QueryAllRawEvents().ToList();

        results.Count.ShouldBe(4);
    }

    #region sample_example_of_querying_for_event_data
    public void example_of_querying_for_event_data(IDocumentSession session, Guid stream)
    {
        var events = session.Events.QueryAllRawEvents()
            .Where(x => x.StreamId == stream)
            .OrderBy(x => x.Sequence)
            .ToList();
    }

    #endregion

    [Fact]
    public void can_fetch_all_events_after_now()
    {
        var now = DateTimeOffset.UtcNow;

        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        var past = now.AddSeconds(-1);

        var results = theSession.Events.QueryAllRawEvents().Where(x => x.Timestamp > past).ToList();

        results.Count.ShouldBe(4);
    }

    [Fact]
    public void can_fetch_all_events_before_now()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        var dbNow = (DateTime)theSession.Connection.CreateCommand().Sql("select now();").ExecuteScalar();
        var now = new DateTimeOffset(dbNow).AddSeconds(5);

        var all = theSession.Events.QueryAllRawEvents().ToList();

        var results = theSession.Events.QueryAllRawEvents()
            .Where(x => x.Timestamp < now).ToList();

        results.Count.ShouldBe(4);
    }

    [Fact]
    public void can_fetch_events_by_sequence()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        theSession.Events.QueryAllRawEvents()
            .Count(x => x.Sequence <= 2).ShouldBe(2);
    }

    [Fact]
    public void can_fetch_by_version()
    {
        theSession.Events.StartStream<Quest>(joined1, departed1);
        theSession.Events.StartStream<Quest>(joined2, departed2);

        theSession.SaveChanges();

        theSession.Events.QueryAllRawEvents()
            .Count(x => x.Version == 1).ShouldBe(2);
    }

    [Fact]
    public void can_search_by_stream()
    {
        var stream1 = theSession.Events.StartStream<Quest>(joined1, departed1).Id;
        var stream2 = theSession.Events.StartStream<Quest>(joined2, departed2).Id;

        theSession.SaveChanges();

        theSession.Events.QueryAllRawEvents()
            .Count(x => x.StreamId == stream1).ShouldBe(2);
    }

    /*
     * MORE!!!
     * Async everything
     */
    public query_against_event_documents_Tests(ITestOutputHelper output)
    {
        _output = output;
        theStore.Advanced.Clean.DeleteAllEventData();
    }
}
