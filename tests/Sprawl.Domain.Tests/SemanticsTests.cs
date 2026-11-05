using Sprawl.Contracts;
using Sprawl.Domain;
using static Sprawl.Domain.Tests.Sandbox;

namespace Sprawl.Domain.Tests;

public sealed class SemanticsTests
{
    [Fact]
    public void Links_and_tags_come_from_prose_not_code()
    {
        Assert.Equal(["Leader election", "cd_00000009"], Semantics.LinkTargets("See [[Leader election]] and [[cd_00000009|that card]], `[[not this]]`\n```\n[[nor this]]\n```"));
        var card = Card("Timeouts #raft", "Matters for #Liveness. Not a heading:\n# Title\nnor https://x.io/#anchor nor `#include` nor #2026.");
        Assert.Equal(["raft", "liveness"], Semantics.Tags(card));
    }

    [Fact]
    public void Links_resolve_by_id_or_title_and_build_backlinks()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp,
            new AddCardIntent(CardKinds.Idea, "Leader election", "Core"),
            new AddCardIntent(CardKinds.Note, "Terms", "Core", Body: "Terms order [[leader election.]] and [[Snapshots]]."));
        var election = canvas.Card("Leader election");
        var terms = canvas.Card("Terms");

        var graph = Semantics.LinkGraph.Build(canvas.State);

        Assert.Equal([election.Id], graph.Outgoing(terms.Id).Outgoing.Select(c => c.Id));
        Assert.Equal(["Snapshots"], graph.Outgoing(terms.Id).Unresolved);
        Assert.Equal([terms.Id], graph.Backlinks(election.Id).Select(c => c.Id));
        Assert.Equal(election.Id, graph.Resolve(election.Id)?.Id);
    }

    [Fact]
    public void Unlinked_mentions_are_cards_that_name_a_title_without_linking_or_connecting()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp,
            new AddCardIntent(CardKinds.Idea, "Log compaction", "Core"),
            new AddCardIntent(CardKinds.Note, "Snapshots", "Core", Body: "Snapshots are how log compaction works."),
            new AddCardIntent(CardKinds.Note, "Linked", "Core", Body: "Uses [[Log compaction]]."),
            new AddCardIntent(CardKinds.Note, "Connected", "Core", Ref: "c", Body: "Log compaction again."),
            new AddCardIntent(CardKinds.Note, "Unrelated", "Core", Body: "Logs compacted are not the phrase."));
        canvas.Run(Mcp, new ConnectIntent(canvas.Card("Connected").Id, canvas.Card("Log compaction").Id));

        var mentions = Semantics.UnlinkedMentions(canvas.State, canvas.Card("Log compaction"));

        Assert.Equal(["Snapshots"], mentions.Select(c => c.Title));
    }

    [Fact]
    public void Linking_a_mention_wraps_the_first_plain_occurrence()
    {
        Assert.Equal("Snapshots are how [[log compaction]] works.", Semantics.LinkFirstMention("Snapshots are how log compaction works.", "Log compaction"));
        Assert.Equal("Nothing here [[Log compaction]]", Semantics.LinkFirstMention("Nothing here", "Log compaction"));
    }

    [Fact]
    public void Related_cards_share_rare_terms_and_skip_cards_already_connected()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp,
            new AddCardIntent(CardKinds.Idea, "Randomized election timeouts", "A", Body: "Followers wait a randomized timeout before starting an election."),
            new AddCardIntent(CardKinds.Note, "Split votes", "B", Body: "Split votes happen when election timeouts collide; randomized timeouts make them rare."),
            new AddCardIntent(CardKinds.Note, "Membership changes", "C", Body: "Joint consensus moves between configurations."),
            new AddCardIntent(CardKinds.Note, "Pizza", "D", Body: "Unrelated topping preferences."),
            new AddCardIntent(CardKinds.Note, "Heartbeats", "E", Body: "Leaders send heartbeats so followers reset the timeout."),
            new AddCardIntent(CardKinds.Note, "Disk", "F", Body: "Persistent storage of terms."));
        var insights = new Insights(canvas.State);

        var related = insights.Related(canvas.Card("Randomized election timeouts"));

        Assert.Equal("Split votes", related[0].Card.Title);
        Assert.Contains("randomized", related[0].SharedTerms);
        Assert.DoesNotContain(related, r => r.Card.Title == "Pizza");

        canvas.Run(Mcp, new ConnectIntent(canvas.Card("Randomized election timeouts").Id, canvas.Card("Split votes").Id));
        Assert.DoesNotContain(new Insights(canvas.State).Related(canvas.Card("Randomized election timeouts")), r => r.Card.Title == "Split votes");
    }

    [Fact]
    public void Analysis_finds_gaps_orphans_dangling_links_questions_contradictions_and_tags()
    {
        var canvas = new Sandbox();
        canvas.Run(Mcp,
            new AddCardIntent(CardKinds.Idea, "Election timeouts", "Leader election", Body: "Randomized election timeouts avoid split votes. #liveness"),
            new AddCardIntent(CardKinds.Idea, "Votes", "Leader election", Body: "A candidate needs a majority of votes in its term. See [[Quorum math]]."),
            new AddCardIntent(CardKinds.Note, "Pre-vote", "Availability", Body: "Pre-vote stops partitioned nodes from disrupting election timeouts and votes. #liveness"),
            new AddCardIntent(CardKinds.Question, "Does pre-vote slow failover?", "Availability"),
            new AddCardIntent(CardKinds.Note, "Cooking", "Elsewhere", Body: "Pasta water should be salty."));
        canvas.Run(Mcp, new ConnectIntent(canvas.Card("Votes").Id, canvas.Card("Election timeouts").Id, ConnectionKinds.Contradicts, "really?"));

        var analysis = new Insights(canvas.State).Analyze(canvas.State.Meta!.Id, 3);

        var gap = Assert.Single(analysis.Gaps);
        Assert.Equal(["Leader election", "Availability"], new[] { gap.FirstTitle, gap.SecondTitle }.OrderBy(t => t == "Availability"));
        Assert.Contains("election", gap.SharedTerms);
        Assert.Contains(analysis.Orphans, c => c.Title == "Cooking");
        Assert.DoesNotContain(analysis.Orphans, c => c.Title == "Votes");
        Assert.Equal("Quorum math", Assert.Single(analysis.DanglingLinks).Target);
        Assert.Equal("Does pre-vote slow failover?", Assert.Single(analysis.OpenQuestions).Title);
        Assert.Equal("really?", Assert.Single(analysis.Contradictions).Label);
        Assert.Equal(new TagCount("liveness", 2), Assert.Single(analysis.Tags));

        // A text link across the gap bridges it.
        canvas.Run(Mcp, new UpdateCardIntent(canvas.Card("Pre-vote").Id, AppendBody: "Related: [[Election timeouts]]"));
        Assert.Empty(new Insights(canvas.State).Gaps());
    }

    private static Card Card(string title, string body) => new("cd_00000001", null, CardKinds.Note, title, body, null, null, null, Emphases.Normal, CardSizes.Regular,
        0, 0, 280, 168, Placements.Auto, "auto", Authors.Ai, Authors.Ai, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
