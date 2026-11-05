using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Sprawl.Mcp.Prompts;

/// <summary>User-invoked workflows (slash commands in Claude). They only describe what to do; the tools do it.</summary>
[McpServerPromptType]
[Authorize(Policy = "scope:canvas.read")]
public sealed class CanvasPrompts
{
    [McpServerPrompt(Name = "expand_region", Title = "Expand a region")]
    [Description("Grow one region of a canvas: find what is missing and add it.")]
    public static string ExpandRegion(
        [Description("The canvas id (cv_…) or its title.")] string canvas,
        [Description("The region to expand (title or id).")] string region,
        [Description("Optional angle to focus on.")] string? focus = null) =>
        $"""
        Expand the "{region}" region of the Sprawl canvas "{canvas}"{(string.IsNullOrWhiteSpace(focus) ? "" : $", focusing on {focus}")}.
        1. If you only have a title, call list_canvases to find the canvas id.
        2. Call get_canvas with region="{region}" and depth=full to see exactly what is already there.
        3. Work out what is missing: core ideas, mechanisms, examples, counterpoints, sources. Add 4-10 atomic cards with add_to_canvas
           (one idea per card, short bodies), plus question cards for the open questions you cannot answer.
        4. Connect each new card to the existing card it most relates to (leads-to, depends-on, example-of, contradicts or relates).
        5. Pass the version from step 2 as expectedVersion, and a requestId.
        Do not repeat existing cards, and do not reorganize the region unless asked. Finish with a two-line summary of what you added.
        """;

    [McpServerPrompt(Name = "review_canvas", Title = "Review a canvas")]
    [Description("Find weak, duplicate, unclear or contradictory cards and propose fixes before applying them.")]
    public static string ReviewCanvas([Description("The canvas id (cv_…) or its title.")] string canvas) =>
        $"""
        Review the Sprawl canvas "{canvas}".
        1. Call get_canvas (depth=short; for a large canvas, read depth=regions first and then each region).
        2. Report, briefly: duplicate or overlapping cards, vague or weak cards, factual errors, contradictions that are not marked,
           cards in the wrong region, orphan cards with no connections, and the most important open questions.
        3. Propose concrete fixes: update_cards for rewrites, reorganize (mergeCards, moveCards, mergeRegions) for structure.
        4. Apply nothing until the owner confirms. When they do, pass the version you read as expectedVersion.
        """;

    [McpServerPrompt(Name = "seed_canvas", Title = "Start a canvas")]
    [Description("Create a new canvas for a topic, seeded with its main regions and first cards.")]
    public static string SeedCanvas([Description("The topic to map.")] string topic) =>
        $"""
        Start a Sprawl canvas for "{topic}".
        1. Call list_canvases with query="{topic}". If a matching canvas exists, say so and offer to expand it instead.
        2. Otherwise call create_canvas with a clear title, a one-sentence description, a few tags, 3-6 regions that divide the topic,
           and 2-5 atomic cards per region, including at least one question card per region for what is still open.
        3. Connect the most important relationships across regions.
        Keep bodies short. Finish with the region names and the open questions.
        """;

    [McpServerPrompt(Name = "find_gaps", Title = "Bridge the gaps")]
    [Description("Find the structural gaps, orphans and missing cards on a canvas, and fill the most valuable ones.")]
    public static string FindGaps([Description("The canvas id (cv_…) or its title.")] string canvas) =>
        $"""
        Strengthen the structure of the Sprawl canvas "{canvas}".
        1. If you only have a title, call list_canvases to find the canvas id. Call analyze_canvas and get_canvas (depth=titles).
        2. For the top gaps (regions that share terms but are not connected), work out the idea or question that links them. Add it as a card
           in the region it fits best and connect it to a card on each side; if nothing honest links them, say so instead.
        3. Write a card for each dangling [[link]] that deserves one (title exactly as linked, so the link resolves).
        4. Connect each orphan to the card it most relates to (use get_card_context to see its related cards), or say why it stands alone.
        5. One add_to_canvas call with refs, expectedVersion and a requestId. Finish with a short list of what you bridged.
        """;

    [McpServerPrompt(Name = "triage_inbox", Title = "Triage the inbox")]
    [Description("Sort the cards captured in a canvas's Inbox region into the regions where they belong.")]
    public static string TriageInbox([Description("The canvas id (cv_…) or its title.")] string canvas) =>
        $"""
        Triage the Inbox of the Sprawl canvas "{canvas}". The owner captures quick thoughts into a region called "Inbox".
        1. Call get_canvas with depth=titles for the structure, then get_canvas with region="Inbox" and depth=full.
        2. For each inbox card decide: move it to the existing region it fits (prefer existing regions; create one only for a cluster of 3+),
           merge it into an existing card it duplicates, or leave it if it is unclear (say why).
        3. Tidy titles that are rough notes with update_cards, and connect moved cards to what they relate to.
        4. Apply with one reorganize call (moveCards, mergeCards, deleteEmptyRegion for the Inbox once empty), passing expectedVersion.
        Finish with a short list: card → where it went.
        """;

    [McpServerPrompt(Name = "answer_questions", Title = "Answer open questions")]
    [Description("Research the owner's open questions that are due for review and add what you find.")]
    public static string AnswerQuestions([Description("Optional: limit to one canvas (id or title).")] string? canvas = null)
    {
        var scope = string.IsNullOrWhiteSpace(canvas) ? "" : " on the Sprawl canvas \"" + canvas + "\"";
        var filter = string.IsNullOrWhiteSpace(canvas) ? "" : " with that canvasId (use list_canvases to find it)";
        return $"""
            Work on the owner's open questions{scope}.
            1. Call get_due_questions{filter}; take the first 3-5.
            2. For each, read its context (get_card_context, and get_canvas for its region at depth=full) so you know what is already there.
            3. Answer what you can: add a note card with the answer in the same region, connected question -leads-to-> answer, citing sources
               as link cards where you used them. If a question splits into narrower ones, add those as question cards instead.
            4. Do not change or delete the question cards themselves; the owner decides when a question is settled.
            Finish with one line per question: answered, partly answered, or still open and why.
            """;
    }

    [McpServerPrompt(Name = "visualize", Title = "Explain a topic with diagrams")]
    [Description("Build or extend a canvas that explains a topic with diagrams (architecture, flows, decisions) beside the cards that explain them.")]
    public static string Visualize(
        [Description("The topic or system to explain, e.g. \"how Kafka works\" or \"our checkout service\".")] string topic,
        [Description("Optional: an existing canvas id (cv_…) or title to add to.")] string? canvas = null) =>
        $"""
        Explain "{topic}" visually on a Sprawl canvas{(string.IsNullOrWhiteSpace(canvas) ? "" : " (" + canvas + ")")}.
        1. Find or create the canvas (list_canvases, then create_canvas with a few regions of explanatory cards if there is none). Read it with get_canvas.
        2. Decide which pictures make the topic click, usually 2-5 of: the architecture (components and how they talk: flowchart LR, people as person,
           external systems as cloud, stores as cylinder, queues and streams as queue, groups as subgraphs), the main request or data flow step by step
           (flowchart TD), the key decision or state logic (diamonds for decisions, labelled yes/no edges), and the lifecycle of the central object.
        3. Draw each with add_diagram, near the region of cards it illustrates. Use thick edges for the main path, dashed for async or optional ones,
           and short edge labels that say what moves (requests, events, rows).
        4. Give each diagram a walkthrough (add_diagram's walkthrough, or add_walkthrough): 4-10 steps in the order a newcomer needs them,
           each lighting up the shapes it talks about. Use sequenceDiagram for interactions over time, stateDiagram-v2 for lifecycles,
           erDiagram for data models and mindmap for an overview; flowcharts for architectures and processes.
           For a system with feedback, add a causal-loop diagram (layout=loops, edges labelled + or -). For an architecture, add a walkthrough
           whose steps mark what fails or changes (marks: down, slow, hot, added, removed, changed) to show its failure modes.
        5. Connect the cards that explain a component to its shape (the refs add_diagram returns name each node) so the canvas reads both ways.
        6. Add question cards for what the diagrams leave open. Finish with one line per diagram: what it shows.
        Read a diagram with get_diagram before changing it; redraw it with add_diagram replace=true, passing expectedVersion.
        """;

    [McpServerPrompt(Name = "teach_me", Title = "Teach me a topic")]
    [Description("Teach a topic step by step on a canvas, checking understanding as you go instead of explaining everything at once.")]
    public static string TeachMe(
        [Description("The topic, e.g. \"virtual functions in C++\".")] string topic,
        [Description("Optional: the canvas id (cv_…) or title to teach on.")] string? canvas = null,
        [Description("Optional: what the learner already knows.")] string? level = null)
    {
        var where = string.IsNullOrWhiteSpace(canvas) ? "a Sprawl canvas for it (find one with list_canvases, or create one)" : "the Sprawl canvas \"" + canvas + "\"";
        var known = string.IsNullOrWhiteSpace(level) ? "" : " The learner says they know: " + level + ".";
        return $"""
            Teach "{topic}" on {where}.{known}
            Work in short rounds, not one long lecture:
            1. Ask one question to find out what the learner already thinks about the topic. Wait for the answer.
            2. Add the next idea to the canvas (one region per stage): a short card or two, plus the picture that makes it click (add_diagram with a
               walkthrough, a table for comparisons, a chart for numbers, or a widget when it is easier to feel by playing with a parameter).
               Show the diagram with show_diagram.
            3. Ask the learner to predict or explain something about it before you do (a cognitive gate: the answer is withheld until they try).
            4. Correct gently, then add a question card for anything they got wrong, so it comes back in their review queue.
            Repeat until the topic is covered, advanced cases included. Finish with a mind map (add_diagram, mindmap) of everything covered.
            """;
    }

    [McpServerPrompt(Name = "check_my_map", Title = "Check a map I drew")]
    [Description("Check a concept map, flowchart or causal-loop diagram the learner drew themselves: what is right, what is missing, what points the wrong way.")]
    public static string CheckMyMap(
        [Description("The canvas id (cv_…) or title.")] string canvas,
        [Description("The region holding the map (title or id).")] string region,
        [Description("Optional: what the map is meant to explain, if the region title does not say.")] string? topic = null)
    {
        var about = string.IsNullOrWhiteSpace(topic) ? "" : " It is meant to explain: " + topic + ".";
        return $"""
            The learner drew the map in region "{region}" of the Sprawl canvas "{canvas}" themselves, to test their understanding.{about}
            1. Read it with get_canvas (depth=full, connections included). Treat it as their work: do not change, move or delete anything in it.
            2. Check it as a tutor would, against what is true about the topic:
               - which ideas and links are right (say so first, specifically);
               - important ideas that are missing;
               - links that are missing, point the wrong way, have the wrong kind or label, or connect things that are not related;
               - in a causal-loop diagram, links with the wrong sign (+ or -), and loops named reinforcing that are balancing or the reverse.
            3. Do not hand over the answer at once. For each problem, ask a question that leads the learner to see it (a cognitive gate), and
               add it as a question card in a region "Map feedback", connected to the item it is about.
            4. Only if the learner asks for it, draw the corrected map as a separate diagram ("{region} (suggested)") next to theirs, with a
               walkthrough whose steps mark what changed (marks: added, removed, changed), so they can compare the two.
            End with a short verdict: what they clearly understand, and the one thing to revisit first.
            """;
    }

    [McpServerPrompt(Name = "quiz_me", Title = "Quiz me (mock interview)")]
    [Description("Quiz the learner from a canvas, one question at a time, grading answers against the cards and recording weak spots.")]
    public static string QuizMe(
        [Description("The canvas id (cv_…) or title.")] string canvas,
        [Description("Optional: limit to one region (title or id).")] string? region = null,
        [Description("Optional: how many questions (default 10).")] int? count = null)
    {
        var scope = string.IsNullOrWhiteSpace(region) ? "" : " (region \"" + region + "\")";
        return $"""
            Run a mock interview from the Sprawl canvas "{canvas}"{scope}, {count ?? 10} questions.
            1. Read it with get_canvas (depth=full for the region you quiz on). Prefer its question cards; otherwise turn its key ideas into questions
               an interviewer would really ask, from easy to hard.
            2. Ask one question at a time. Never show the answer before the learner replies.
            3. Grade each reply against the cards: what was right, what was missing, the crisp interview answer. If the answer needs a picture, show the
               relevant diagram with show_diagram.
            4. For every miss, add a question card with the exact question (and a note card with the model answer, connected question -leads-to-> note)
               to an "Interview misses" region, so it comes back in the learner's review queue.
            End with a score, the three weakest areas, and which regions to revisit.
            """;
    }

    [McpServerPrompt(Name = "synthesize_region", Title = "Synthesize a region")]
    [Description("Distil a region into one key summary card, linked to the cards it draws on.")]
    public static string SynthesizeRegion(
        [Description("The canvas id (cv_…) or its title.")] string canvas,
        [Description("The region to synthesize (title or id).")] string region) =>
        $"""
        Synthesize the "{region}" region of the Sprawl canvas "{canvas}".
        1. Call get_canvas with region="{region}" and depth=full.
        2. Write one note card titled "Synthesis: <region>" with emphasis key: the 3-6 claims the region supports, each citing the cards it
           rests on as [[Card title]] links, then what is still contested (cite contradicting cards) and what is still open (cite questions).
        3. Add it to the region with add_to_canvas (expectedVersion, requestId). If a synthesis card exists, update it with update_cards instead.
        Do not restate every card; the synthesis is what someone reads first.
        """;
}
