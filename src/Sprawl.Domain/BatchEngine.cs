using System.Text.RegularExpressions;
using Sprawl.Contracts;

namespace Sprawl.Domain;

/// <summary>Who is writing. The owner is a human in the UI; the MCP device principal is AI.</summary>
public sealed record Actor(string Kind, string? Client = null)
{
    public static readonly Actor Owner = new(ActorKinds.Owner);
    public bool IsOwner => Kind == ActorKinds.Owner;
    public string Author => IsOwner ? Authors.Human : Authors.Ai;
}

public sealed record EngineResult(
    List<Op> Ops,
    Dictionary<string, string> Refs,
    List<SkippedItem> Skipped,
    List<string> Warnings,
    string Summary);

/// <summary>
/// Resolves a batch of semantic intents against a canvas state into operations. Pure: the input state is not modified,
/// and the same state, request, clock and ID sequence always produce the same operations. Any refusal throws
/// <see cref="SprawlException"/> and nothing is applied (batches are atomic).
/// </summary>
public sealed partial class BatchEngine
{
    private readonly CanvasState original;
    private readonly CanvasState state;
    private readonly BatchRequest request;
    private readonly Actor actor;
    private readonly DateTimeOffset now;
    private readonly IIdGenerator ids;
    private readonly LayoutEngine layout;
    private readonly Dictionary<string, string> refs = new(StringComparer.Ordinal);
    private readonly List<SkippedItem> skipped = [];
    private readonly List<string> notes = [];
    private int index;

    private int maxIntents = Limits.IntentsPerBatch;
    private long sequence;

    private BatchEngine(CanvasState original, BatchRequest request, Actor actor, DateTimeOffset now, IIdGenerator ids)
    {
        this.original = original;
        state = original.Clone();
        this.request = request;
        this.actor = actor;
        this.now = now;
        this.ids = ids;
        layout = new LayoutEngine(state);
    }

    public static EngineResult Execute(CanvasState current, BatchRequest request, Actor actor, DateTimeOffset now, IIdGenerator ids)
    {
        if (current.Meta is null) throw SprawlException.NotFound("The canvas does not exist.");
        return new BatchEngine(current, request, actor, now, ids).Run();
    }

    /// <summary>A new canvas: the create op for its metadata plus an optional seed batch, as one version.</summary>
    public static EngineResult Create(string canvasId, CreateCanvasRequest create, Actor actor, DateTimeOffset now, IIdGenerator ids, int maxIntents = Limits.IntentsPerBatch)
    {
        var meta = new CanvasMeta(canvasId, Text.Title(create.Title, Limits.CanvasTitle, "title"), Text.Description(create.Description),
            Text.Tags(create.Tags ?? []), Text.Collection(create.Collection), false, false, now);
        var empty = new CanvasState();
        var seeded = new CanvasState { Meta = meta };
        var batch = new BatchRequest(create.Intents ?? [], create.RequestId, BatchKinds.Create, OnDuplicate: create.OnDuplicate);
        var engine = new BatchEngine(seeded, batch, actor, now, ids) { maxIntents = maxIntents };
        var result = engine.Run();
        var ops = StateOps.Diff(empty, engine.state);
        return result with { Ops = ops, Summary = "Created canvas" + (result.Ops.Count > 0 ? "; " + result.Summary : "") };
    }

    private EngineResult Run()
    {
        if (request.Intents.Count > maxIntents) throw SprawlException.Limit($"A batch holds at most {maxIntents} intents.");
        if (request.OnDuplicate is not (DuplicatePolicies.Skip or DuplicatePolicies.Add or DuplicatePolicies.Fail))
            throw SprawlException.Invalid("onDuplicate must be skip, add or fail.");
        var kind = request.Kind ?? BatchKinds.Edit;
        if (kind == BatchKinds.Tidy && (!actor.IsOwner || request.Intents.Any(intent => intent is not SetLayoutIntent)))
            throw SprawlException.Forbidden("A tidy batch holds only owner layout changes.");
        for (index = 0; index < request.Intents.Count; index++)
        {
            try { Apply(request.Intents[index]); }
            catch (SprawlException error) when (error.Status is 400 or 404 && request.Intents.Count > 1)
            {
                throw new SprawlException(error.Code, $"Intent {index + 1} ({Describe(request.Intents[index])}): {error.Message}", error.Status) { Entities = error.Entities };
            }
            layout.Settle();
        }
        if (state.Cards.Count > Limits.CardsPerCanvas) throw SprawlException.Limit($"A canvas holds at most {Limits.CardsPerCanvas} cards.");
        if (state.Regions.Count > Limits.RegionsPerCanvas) throw SprawlException.Limit($"A canvas holds at most {Limits.RegionsPerCanvas} regions.");
        if (state.Regions.Values.Any(r => RegionLayouts.IsDiagram(r.Layout)))
        {
            DiagramLayout.Apply(state, layout);
            layout.Settle();
        }
        // A sketch lives inside its region: when a merge or delete removes the region, its sketches go with it.
        foreach (var orphan in state.Sketches.Values.Where(s => s.RegionId is not null && !state.Regions.ContainsKey(s.RegionId)).ToList()) state.Sketches.Remove(orphan.Id);
        foreach (var orphan in state.Walkthroughs.Values.Where(w => !state.Regions.ContainsKey(w.RegionId)).ToList()) state.Walkthroughs.Remove(orphan.Id);
        // A detail link to a region that is gone is dropped.
        foreach (var card in state.Cards.Values.Where(c => c.Detail is not null && !state.Regions.ContainsKey(c.Detail)).ToList())
            state.Cards[card.Id] = card with { Detail = null };
        if (state.Walkthroughs.Count > Limits.WalkthroughsPerCanvas) throw SprawlException.Limit($"A canvas holds at most {Limits.WalkthroughsPerCanvas} walkthroughs.");
        if (state.Sketches.Count > Limits.SketchesPerCanvas) throw SprawlException.Limit($"A canvas holds at most {Limits.SketchesPerCanvas} sketches.");
        if (state.Connections.Count > Limits.ConnectionsPerCanvas) throw SprawlException.Limit($"A canvas holds at most {Limits.ConnectionsPerCanvas} connections.");
        var ops = StateOps.Diff(original, state);
        return new(ops, refs, skipped, [.. notes, .. layout.Warnings], Summaries.Describe(ops, original, state, kind));
    }

    private static string Describe(Intent intent) => intent.GetType().Name.Replace("Intent", "", StringComparison.Ordinal);

    private void Apply(Intent intent)
    {
        switch (intent)
        {
            case AddRegionIntent add: AddRegion(add); break;
            case AddCardIntent add: AddCard(add); break;
            case UpdateCardIntent update: UpdateCard(update); break;
            case ConnectIntent connect: Connect(connect); break;
            case DisconnectIntent disconnect: Disconnect(disconnect); break;
            case MoveCardsIntent move: MoveCards(move); break;
            case UpdateRegionIntent update: UpdateRegion(update); break;
            case NestRegionIntent nest: NestRegion(nest); break;
            case MergeRegionsIntent merge: MergeRegions(merge); break;
            case MergeCardsIntent merge: MergeCards(merge); break;
            case DeleteRegionIntent delete: DeleteRegion(delete); break;
            case DeleteCardsIntent delete: DeleteCards(delete); break;
            case UpdateCanvasIntent update: UpdateCanvas(update); break;
            case SetLayoutIntent set: SetLayout(set); break;
            case UnpinIntent unpin: Unpin(unpin); break;
            case AddSketchIntent add: AddSketch(add); break;
            case DeleteSketchesIntent delete: DeleteSketches(delete); break;
            case UpdateConnectionIntent update: UpdateConnection(update); break;
            case AddDiagramIntent diagram: AddDiagram(diagram); break;
            case AddWalkthroughIntent walkthrough: AddWalkthrough(walkthrough); break;
            case DeleteWalkthroughIntent delete: if (!state.Walkthroughs.Remove(delete.Id)) skipped.Add(new("missing", null, delete.Id)); break;
            default: throw SprawlException.Invalid("Unknown intent.");
        }
    }

    // ---- Regions -------------------------------------------------------------------------------------------------

    private void AddRegion(AddRegionIntent add)
    {
        var title = Text.Title(add.Title, Limits.RegionTitle, "region title");
        Region? parent = null;
        if (add.Parent is not null)
        {
            parent = ResolveRegion(add.Parent) ?? throw SprawlException.NotFound($"Parent region '{add.Parent}' was not found.");
            if (parent.ParentId is not null) throw SprawlException.Invalid("Regions nest one level only; the parent is already a sub-region.");
        }
        var existing = state.RegionsIn(parent?.Id).FirstOrDefault(region => Text.SameTitle(region.Title, title));
        if (existing is not null)
        {
            MapRef(add.Ref, existing.Id);
            skipped.Add(new("region_exists", add.Ref, null, existing.Id));
            return;
        }
        var anchor = add.Near is null ? null : (ResolveRegion(add.Near) ?? throw SprawlException.NotFound($"Region '{add.Near}' was not found."));
        CreateRegion(title, add.Ref, parent?.Id, Text.Tint(add.Tint), anchor?.ParentId ?? anchor?.Id, LookaheadHeights(add.Ref, title, index + 1), Text.Layout(add.Layout));
    }

    private Region CreateRegion(string title, string? reference, string? parentId, string? tint, string? anchorId, List<int> heights, string? regionLayout = null)
    {
        if (reference is not null) ValidateRef(reference);
        var cols = RegionLayouts.IsDiagram(regionLayout) ? 1 : Geometry.ColsFor(heights.Count);
        var (width, height) = Geometry.EstimateRegion(cols, RegionLayouts.IsDiagram(regionLayout) ? [] : heights);
        var id = NewId(EntityKind.Region);
        state.Regions[id] = new Region(id, parentId, title, tint, cols, 0, 0, width, height, Placements.Auto, actor.Author, now, now, regionLayout);
        layout.PlaceRegion(id, parentId is null ? anchorId ?? LookaheadAnchor(reference, title) : null);
        layout.Touch(id);
        MapRef(reference, id);
        return state.Regions[id];
    }

    /// <summary>Heights of the cards later in this batch that target a region being created, so it is placed at its final size.</summary>
    private List<int> LookaheadHeights(string? reference, string title, int from)
    {
        var heights = new List<int>();
        for (var i = from; i < request.Intents.Count; i++)
            if (request.Intents[i] is AddCardIntent { At: null } card && card.Region is not null && TargetsNew(card.Region, reference, title))
                heights.Add(Geometry.HeightOf(Geometry.SizeFor(card.Kind, card.Body ?? "")));
        return heights;
    }

    private bool TargetsNew(string target, string? reference, string title) =>
        (reference is not null && target == reference) || Text.SameTitle(target, title);

    /// <summary>The existing top-level region most connected (in this batch) to the cards destined for a new region.</summary>
    private string? LookaheadAnchor(string? reference, string title)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        if (reference is not null) members.Add(reference);
        foreach (var intent in request.Intents.Skip(index))
            if (intent is AddCardIntent { Ref: { } cardRef, Region: { } target } && TargetsNew(target, reference, title)) members.Add(cardRef);
        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var intent in request.Intents.Skip(index))
        {
            if (intent is not ConnectIntent connect) continue;
            var other = members.Contains(connect.From) ? connect.To : members.Contains(connect.To) ? connect.From : null;
            if (other is null || TopLevelRegionOf(other) is not { } anchor) continue;
            if (!votes.TryAdd(anchor, 1)) votes[anchor]++; else order.Add(anchor);
        }
        return order.OrderByDescending(id => votes[id]).FirstOrDefault();
    }

    private string? TopLevelRegionOf(string reference)
    {
        var id = refs.GetValueOrDefault(reference) ?? reference;
        var regionId = EntityIds.KindOf(id) switch
        {
            EntityKind.Card => state.Cards.GetValueOrDefault(id)?.RegionId,
            EntityKind.Region => state.Regions.ContainsKey(id) ? id : null,
            _ => null
        };
        if (regionId is null) return null;
        return state.Regions[regionId].ParentId ?? regionId;
    }

    private void UpdateRegion(UpdateRegionIntent update)
    {
        var region = RequireRegion(update.Id);
        var updated = region;
        if (update.Title is not null)
        {
            var title = Text.Title(update.Title, Limits.RegionTitle, "region title");
            if (state.RegionsIn(region.ParentId).Any(other => other.Id != region.Id && Text.SameTitle(other.Title, title)))
                throw new SprawlException(ErrorCodes.Duplicate, $"A region named '{title}' already exists here.", 409);
            updated = updated with { Title = title };
        }
        if (update.Tint is not null) updated = updated with { Tint = Text.Tint(update.Tint) };
        if (update.Layout is not null) updated = updated with { Layout = Text.Layout(update.Layout) };
        if (updated == region) return;
        state.Regions[region.Id] = updated with { UpdatedAt = now };
        if (updated.Layout == region.Layout) return;
        // A new layout re-sizes the region's cards for it and hands them back to automatic placement.
        if (!RegionLayouts.IsDiagram(updated.Layout)) state.Regions[region.Id] = state.Regions[region.Id] with { Cols = Geometry.ColsFor(state.CardsIn(region.Id).Count()) };
        var cards = state.CardsIn(region.Id).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        // Park them all clear of every column first, so cards still at their old spots do not block the first ones placed.
        foreach (var card in cards) state.Cards[card.Id] = card with { X = -100_000, Y = Geometry.Header };
        foreach (var card in cards.Select(c => state.Cards[c.Id]))
        {
            layout.Touch(card.Id);
            state.Cards[card.Id] = Sized(card with { Placement = Placements.Auto });
            layout.PlaceCard(card.Id);
            layout.Touch(card.Id);
        }
    }

    private void NestRegion(NestRegionIntent nest)
    {
        var region = RequireRegion(nest.Id);
        Region? parent = null;
        if (nest.Parent is not null)
        {
            parent = RequireRegion(nest.Parent);
            if (parent.Id == region.Id) throw SprawlException.Invalid("A region cannot contain itself.");
            if (parent.ParentId is not null) throw SprawlException.Invalid("Regions nest one level only; the parent is already a sub-region.");
            if (state.RegionsIn(region.Id).Any()) throw SprawlException.Invalid("A region with sub-regions cannot become a sub-region.");
        }
        if (region.ParentId == parent?.Id) return;
        if (state.RegionsIn(parent?.Id).Any(other => Text.SameTitle(other.Title, region.Title)))
            throw new SprawlException(ErrorCodes.Duplicate, $"A region named '{region.Title}' already exists there.", 409);
        if (!actor.IsOwner && region.Placement == Placements.Manual)
        {
            if (request.SkipPinned) { skipped.Add(new("pinned", null, region.Id)); return; }
            throw SprawlException.Pinned($"Region '{region.Title}' was placed by hand and cannot be moved.", [region.Id]);
        }
        var formerParent = region.ParentId;
        layout.Touch(region.Id);
        state.Regions[region.Id] = region with { ParentId = parent?.Id, Placement = Placements.Auto, UpdatedAt = now };
        layout.PlaceRegion(region.Id, parent is null ? formerParent : null);
        layout.Touch(region.Id);
    }

    private void MergeRegions(MergeRegionsIntent merge)
    {
        var into = RequireRegion(merge.IntoId);
        foreach (var sourceRef in merge.SourceIds)
        {
            var source = RequireRegion(sourceRef);
            if (source.Id == into.Id) throw SprawlException.Invalid("A region cannot be merged into itself.");
            if (into.ParentId == source.Id) throw SprawlException.Invalid("A region cannot be merged into its own sub-region.");
            foreach (var card in state.CardsIn(source.Id).OrderBy(card => card.Y).ThenBy(card => card.X).ThenBy(card => card.Id, StringComparer.Ordinal).ToList())
                MoveCard(card, into.Id);
            foreach (var sub in state.RegionsIn(source.Id).OrderBy(sub => sub.Y).ThenBy(sub => sub.X).ToList())
            {
                if (into.ParentId is not null) throw SprawlException.Invalid("Sub-regions cannot be merged into a sub-region.");
                NestRegion(new NestRegionIntent(sub.Id, into.Id));
            }
            if (state.CardsIn(source.Id).Any() || state.RegionsIn(source.Id).Any()) continue;
            Rewire(source.Id, into.Id);
            RemoveRegion(source.Id);
        }
    }

    private void DeleteRegion(DeleteRegionIntent delete)
    {
        var region = RequireRegion(delete.Id);
        if (state.CardsIn(region.Id).Any() || state.RegionsIn(region.Id).Any())
            throw SprawlException.Conflict($"Region '{region.Title}' is not empty; move or delete its cards first.", [region.Id]);
        RemoveRegion(region.Id);
    }

    private void RemoveRegion(string id)
    {
        foreach (var connection in state.Connections.Values.Where(c => c.SourceId == id || c.TargetId == id).ToList()) state.Connections.Remove(connection.Id);
        layout.Touch(id);
        state.Regions.Remove(id);
    }

    // ---- Cards ---------------------------------------------------------------------------------------------------

    private void AddCard(AddCardIntent add) => AddCard(add, diagramNode: false);

    private void AddCard(AddCardIntent add, bool diagramNode)
    {
        if (add.At is not null && !actor.IsOwner) throw SprawlException.Forbidden("Only the owner places cards by position.");
        if (add.Ref is not null) ValidateRef(add.Ref);
        var kind = Text.Kind(add.Kind);
        var title = Text.Title(add.Title, Limits.CardTitle, "card title");
        var body = Text.Body(add.Body, kind);
        var url = Text.Url(add.Url, kind);
        var language = Text.Language(add.Language);
        Region? region = null;
        if (add.Region is not null)
        {
            region = ResolveRegion(add.Region);
            if (region is null)
            {
                if (!request.CreateMissingRegions || LooksLikeReference(add.Region)) throw SprawlException.NotFound($"Region '{add.Region}' was not found.");
                region = CreateRegion(Text.Title(add.Region, Limits.RegionTitle, "region title"), null, null, null, null, LookaheadHeights(null, add.Region, index));
            }
        }
        else if (add.At is null) throw SprawlException.Invalid("A card needs a region.");

        if (!diagramNode && request.OnDuplicate != DuplicatePolicies.Add && state.CardsIn(region?.Id).FirstOrDefault(card => Text.SameTitle(card.Title, title)) is { } duplicate)
        {
            if (request.OnDuplicate == DuplicatePolicies.Fail)
                throw new SprawlException(ErrorCodes.Duplicate, $"A card titled '{duplicate.Title}' already exists in this region ({duplicate.Id}).", 409) { Entities = [duplicate.Id] };
            MapRef(add.Ref, duplicate.Id);
            skipped.Add(new("duplicate", add.Ref, null, duplicate.Id));
            return;
        }

        var size = Geometry.SizeFor(kind, body);
        var id = NewId(EntityKind.Card);
        var card = Sized(new Card(id, region?.Id, kind, title, body, url, language, Text.Tint(add.Tint), Text.Emphasis(add.Emphasis), size,
            0, 0, Geometry.CardWidth, Geometry.HeightOf(size), Placements.Auto, Placements.Auto, actor.Author, actor.Author, now, now, Text.Shape(add.Shape),
            add.Detail is null ? null : RequireRegion(add.Detail).Id, Text.Icon(add.Icon)));
        if (add.At is { } at)
        {
            var (x, y) = Clamp(at.X, at.Y, region is not null);
            state.Cards[id] = card with { X = x, Y = y, Placement = Placements.Manual };
        }
        else
        {
            state.Cards[id] = card;
            layout.PlaceCard(id);
        }
        layout.Touch(id);
        MapRef(add.Ref, id);
        // Duplicates are skipped only within a region; elsewhere on the canvas the same title is allowed but flagged,
        // so a caller that read a single region learns about it (and can merge with reorganize/mergeCards).
        if (!diagramNode && state.Cards.Values.FirstOrDefault(other => other.Id != id && other.RegionId != region?.Id && Text.SameTitle(other.Title, title)) is { } twin)
        {
            var where = twin.RegionId is { } twinRegion && state.Regions.TryGetValue(twinRegion, out var r) ? $"region '{r.Title}'" : "the open canvas";
            notes.Add($"'{title}' ({id}) has the same title as {twin.Id} in {where}; if they are the same idea, merge them with reorganize mergeCards.");
        }
    }

    private void UpdateCard(UpdateCardIntent update)
    {
        var card = RequireCard(update.Id);
        if (update.Body is not null && update.AppendBody is not null) throw SprawlException.Invalid("Give body or appendBody, not both.");
        var kind = update.Kind is null ? card.Kind : Text.Kind(update.Kind);
        var body = update.Body ?? (update.AppendBody is null ? card.Body : (card.Body.TrimEnd() + "\n\n" + update.AppendBody.Trim()).Trim());
        var updated = card with
        {
            Kind = kind,
            Title = update.Title is null ? card.Title : Text.Title(update.Title, Limits.CardTitle, "card title"),
            Body = Text.Body(body, kind),
            Url = update.Url is null ? (kind == CardKinds.Link ? Text.Url(card.Url, kind) : card.Url) : Text.Url(update.Url, kind),
            Language = update.Language is null ? card.Language : Text.Language(update.Language),
            Emphasis = update.Emphasis is null ? card.Emphasis : Text.Emphasis(update.Emphasis),
            Tint = update.Tint is null ? card.Tint : Text.Tint(update.Tint),
            Shape = update.Shape is null ? card.Shape : Text.Shape(update.Shape),
            Detail = update.Detail is null ? card.Detail : update.Detail.Trim().ToLowerInvariant() == "none" ? null : RequireRegion(update.Detail).Id,
            Icon = update.Icon is null ? card.Icon : Text.Icon(update.Icon)
        };
        if (updated == card) return;
        Resize(card, updated with { UpdatedAt = now, UpdatedBy = actor.Author });
    }

    private void Resize(Card before, Card updated)
    {
        updated = Sized(updated);
        state.Cards[before.Id] = updated;
        if (updated.H != before.H || updated.W != before.W) layout.Touch(before.Id);
    }

    private void MoveCards(MoveCardsIntent move)
    {
        var target = ResolveRegion(move.ToRegion);
        if (target is null)
        {
            if (!(move.CreateRegion || request.CreateMissingRegions) || LooksLikeReference(move.ToRegion))
                throw SprawlException.NotFound($"Region '{move.ToRegion}' was not found.");
            var heights = move.Ids.Select(id => ResolveCardOrNull(id)).Where(card => card is not null).Select(card => card!.H).ToList();
            target = CreateRegion(Text.Title(move.ToRegion, Limits.RegionTitle, "region title"), null, null, null, null, heights);
        }
        foreach (var reference in move.Ids) MoveCard(RequireCard(reference), target.Id);
    }

    private void MoveCard(Card card, string regionId)
    {
        if (card.RegionId == regionId) return;
        if (!actor.IsOwner && (card.Placement == Placements.Manual || card.RegionId is null))
        {
            if (request.SkipPinned) { skipped.Add(new("pinned", null, card.Id)); return; }
            throw SprawlException.Pinned($"Card '{card.Title}' was placed by hand and cannot be moved.", [card.Id]);
        }
        layout.Touch(card.Id);
        state.Cards[card.Id] = Sized(card with { RegionId = regionId, Placement = Placements.Auto });
        layout.PlaceCard(card.Id);
        layout.Touch(card.Id);
    }

    private void MergeCards(MergeCardsIntent merge)
    {
        var into = RequireCard(merge.Into);
        var sources = merge.Ids.Select(RequireCard).Where(card => card.Id != into.Id).DistinctBy(card => card.Id).ToList();
        if (sources.Count == 0) throw SprawlException.Invalid("Name at least one card to merge into the target.");
        var body = merge.Body ?? string.Join("\n\n---\n\n", sources.Select(card => $"**{card.Title}**" + (card.Body.Length > 0 ? "\n\n" + card.Body : ""))
            .Prepend(into.Body).Where(part => part.Length > 0));
        var merged = into with
        {
            Title = merge.Title is null ? into.Title : Text.Title(merge.Title, Limits.CardTitle, "card title"),
            Body = Text.Body(body, into.Kind),
            UpdatedAt = now,
            UpdatedBy = actor.Author
        };
        foreach (var source in sources)
        {
            Rewire(source.Id, into.Id);
            layout.Touch(source.Id);
            state.Cards.Remove(source.Id);
        }
        Resize(into, merged);
    }

    private void DeleteCards(DeleteCardsIntent delete)
    {
        foreach (var reference in delete.Ids)
        {
            var card = ResolveCardOrNull(reference);
            if (card is null) { skipped.Add(new("missing", null, reference)); continue; }
            foreach (var connection in state.Connections.Values.Where(c => c.SourceId == card.Id || c.TargetId == card.Id).ToList())
                state.Connections.Remove(connection.Id);
            layout.Touch(card.Id);
            state.Cards.Remove(card.Id);
        }
    }

    // ---- Connections ---------------------------------------------------------------------------------------------

    private void Connect(ConnectIntent connect)
    {
        var from = ResolveEndpoint(connect.From);
        var to = ResolveEndpoint(connect.To);
        if (from == to) throw SprawlException.Invalid("A connection needs two different endpoints.");
        var kind = connect.Kind is null ? ConnectionKinds.Relates : Text.OneOf(connect.Kind, ConnectionKinds.All, "connection kind");
        var label = Text.Label(connect.Label);
        var line = Text.Line(connect.Line);
        var ends = Text.Ends(connect.Ends);
        var existing = state.Connections.Values.FirstOrDefault(c => c.SourceId == from && c.TargetId == to && c.Kind == kind);
        if (existing is not null)
        {
            var restyled = existing with
            {
                Label = connect.Label is null ? existing.Label : label,
                Line = connect.Line is null ? existing.Line : line,
                Ends = connect.Ends is null ? existing.Ends : ends
            };
            if (restyled != existing) state.Connections[existing.Id] = restyled;
            return;
        }
        var id = NewId(EntityKind.Connection);
        state.Connections[id] = new Connection(id, from, to, kind, label, actor.Author, now, line, ends);
    }

    private void UpdateConnection(UpdateConnectionIntent update)
    {
        var connection = state.Connections.GetValueOrDefault(update.Id) ?? throw SprawlException.NotFound($"Connection '{update.Id}' was not found.");
        var updated = connection with
        {
            Kind = update.Kind is null ? connection.Kind : Text.OneOf(update.Kind, ConnectionKinds.All, "connection kind"),
            Label = update.Label is null ? connection.Label : Text.Label(update.Label.Length == 0 ? null : update.Label),
            Line = update.Line is null ? connection.Line : Text.Line(update.Line),
            Ends = update.Ends is null ? connection.Ends : Text.Ends(update.Ends)
        };
        if (updated != connection) state.Connections[connection.Id] = updated;
    }

    /// <summary>
    /// A Mermaid flowchart as a diagram region: one sub-region per subgraph (when the diagram is top-level, since regions
    /// nest one level), one shaped card per node (titles may repeat, as "Yes" and "No" do), one connection per edge.
    /// Node refs are <c>{ref}:{nodeId}</c>; the diagram pass at the end of the batch lays it all out.
    /// </summary>
    private void AddDiagram(AddDiagramIntent add)
    {
        var type = Mermaid.DiagramType(add.Mermaid);
        var chart = type == Mermaid.Flowchart ? Mermaid.Parse(add.Mermaid) : new Mermaid.Chart(RegionLayouts.FlowDown, [], [], []);
        var flow = add.Layout is null ? chart.Layout : Text.Layout(add.Layout);
        if (!RegionLayouts.IsDiagram(flow)) flow = chart.Layout;
        var prefix = add.Ref ?? $"diagram{index + 1}";
        ValidateRef(prefix);
        var existed = state.RegionsIn(add.Parent is null ? null : ResolveRegion(add.Parent)?.Id).FirstOrDefault(r => Text.SameTitle(r.Title, add.Title));
        if (existed is not null && add.Replace)
        {
            var oldGroups = state.RegionsIn(existed.Id).Select(r => r.Id).ToList();
            var cards = state.Cards.Values.Where(c => c.RegionId == existed.Id || (c.RegionId is not null && oldGroups.Contains(c.RegionId))).Select(c => c.Id).ToList();
            if (!actor.IsOwner && state.Cards.Values.Any(c => cards.Contains(c.Id) && c.Placement == Placements.Manual))
                throw SprawlException.Pinned($"Diagram '{existed.Title}' has shapes the owner placed by hand; edit it with update_cards and connect instead.", [existed.Id]);
            DeleteCards(new DeleteCardsIntent(cards));
            foreach (var group in oldGroups)
            {
                foreach (var c in state.Connections.Values.Where(c => c.SourceId == group || c.TargetId == group).ToList()) state.Connections.Remove(c.Id);
                layout.Touch(group);
                state.Regions.Remove(group);
            }
        }
        AddRegion(new AddRegionIntent(add.Title, prefix, add.Parent, null, add.Near, flow));
        var region = state.Regions[refs[prefix]];
        // A card region becomes a diagram; a redraw also takes the new direction.
        if (!RegionLayouts.IsDiagram(region.Layout) || (add.Replace && region.Layout != flow))
        {
            UpdateRegion(new UpdateRegionIntent(region.Id, Layout: flow));
            region = state.Regions[region.Id];
        }
        var nested = region.ParentId is null;
        if (!nested && chart.Subgraphs.Count > 0) notes.Add($"Diagram '{region.Title}' is a sub-region, so its subgraphs were flattened into it.");

        string NodeRef(string nodeId) => prefix.Length + nodeId.Length + 1 <= Limits.Ref ? $"{prefix}:{nodeId}" : $"{prefix}:#{StableHash(nodeId)}";
        string GroupRef(string groupId) => prefix.Length + groupId.Length + 2 <= Limits.Ref ? $"{prefix}/{groupId}" : $"{prefix}/#{StableHash(groupId)}";
        if (add.DetailOf is not null)
        {
            var owner = RequireCard(add.DetailOf);
            if (owner.RegionId == region.Id) throw SprawlException.Invalid("A diagram cannot be the detail of one of its own shapes.");
            state.Cards[owner.Id] = owner with { Detail = region.Id, UpdatedAt = now, UpdatedBy = actor.Author };
        }
        if (type != Mermaid.Flowchart)
        {
            // Sequence, class, ER, state, mind map, timeline, …: one card holding the source, rendered by the browser.
            AddCard(new AddCardIntent(CardKinds.Mermaid, Clip(add.Title, Limits.CardTitle), region.Id, $"{prefix}:diagram", add.Mermaid.Trim()), diagramNode: true);
            return;
        }
        if (nested)
            foreach (var group in chart.Subgraphs)
                AddRegion(new AddRegionIntent(Clip(group.Title, Limits.RegionTitle), GroupRef(group.Id), region.Id, null, null, group.Layout ?? flow));
        foreach (var node in chart.Nodes)
        {
            var target = nested && node.Subgraph is not null ? GroupRef(node.Subgraph) : region.Id;
            var title = Clip(node.Label, Limits.CardTitle);
            AddCard(new AddCardIntent(CardKinds.Note, title, target, NodeRef(node.Id), title == node.Label ? null : node.Label, Shape: node.Shape, Icon: node.Icon), diagramNode: true);
            // Everything in one batch shares a timestamp; a tick per node keeps the Mermaid order, which the layout and the outline read.
            var created = state.Cards[refs[NodeRef(node.Id)]];
            state.Cards[created.Id] = created with { CreatedAt = now.AddTicks(++sequence), UpdatedAt = now.AddTicks(sequence) };
        }
        var groups = chart.Subgraphs.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        string EndRef(string id) => groups.Contains(id) ? (nested ? GroupRef(id) : region.Id) : NodeRef(id);
        foreach (var edge in chart.Edges)
        {
            var (from, to) = (EndRef(edge.From), EndRef(edge.To));
            if (refs.GetValueOrDefault(from) == refs.GetValueOrDefault(to)) continue;
            var kind = edge.Ends == EndStyles.End ? ConnectionKinds.LeadsTo : ConnectionKinds.Relates;
            var ends = edge.Ends is EndStyles.End or EndStyles.None ? null : edge.Ends;
            var before = state.Connections.Keys.ToHashSet(StringComparer.Ordinal);
            Connect(new ConnectIntent(from, to, kind, edge.Label is null ? null : Clip(edge.Label, Limits.Label), edge.Line == LineStyles.Solid ? null : edge.Line, ends));
            foreach (var id in state.Connections.Keys.Where(id => !before.Contains(id)).ToList())
                state.Connections[id] = state.Connections[id] with { CreatedAt = now.AddTicks(++sequence) };
        }
    }

    /// <summary>FNV-1a, so a long node id maps to the same short ref in every process (string.GetHashCode is randomised).</summary>
    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text) hash = (hash ^ c) * 16777619u;
        return hash;
    }

    /// <summary>
    /// A walkthrough's steps, with every highlighted name resolved now (refs from this batch, ids, or titles inside the
    /// region), so a reader never has to guess what "the gateway" meant. A name that matches nothing fails the batch.
    /// </summary>
    private void AddWalkthrough(AddWalkthroughIntent add)
    {
        var region = RequireRegion(add.Region);
        var title = Text.Title(add.Title, Limits.RegionTitle, "walkthrough title");
        if (add.Steps.Count == 0 || add.Steps.Count > Limits.WalkthroughSteps) throw SprawlException.Invalid($"A walkthrough has 1-{Limits.WalkthroughSteps} steps.");
        var scope = state.Regions.Values.Where(r => r.Id == region.Id || r.ParentId == region.Id).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var steps = new List<WalkthroughStep>();
        foreach (var (step, number) in add.Steps.Select((s, i) => (s, i + 1)))
        {
            var text = (step.Text ?? "").Trim();
            if (text.Length == 0 || text.Length > Limits.StepText) throw SprawlException.Invalid($"Step {number}: the text is 1-{Limits.StepText} characters.");
            var names = step.Highlight ?? [];
            if (names.Count > Limits.StepHighlight) throw SprawlException.Limit($"Step {number}: highlight at most {Limits.StepHighlight} items.");
            var ids = new List<string>();
            foreach (var name in names)
            {
                var id = ResolveHighlight(name.Trim(), scope)
                    ?? throw SprawlException.NotFound($"Step {number}: nothing in '{region.Title}' is called '{name}'. Use a card or region title, a ref from this call, or an id.");
                if (!ids.Contains(id)) ids.Add(id);
            }
            var marks = new List<StepMark>();
            if ((step.Marks ?? []).Count > Limits.StepHighlight) throw SprawlException.Limit($"Step {number}: mark at most {Limits.StepHighlight} items.");
            foreach (var mark in step.Marks ?? [])
            {
                var target = ResolveHighlight((mark.Target ?? "").Trim(), scope)
                    ?? throw SprawlException.NotFound($"Step {number}: nothing in '{region.Title}' is called '{mark.Target}' to mark.");
                var markState = Text.OneOf((mark.State ?? "").Trim().ToLowerInvariant(), MarkStates.All, "mark state");
                var note = string.IsNullOrWhiteSpace(mark.Note) ? null : Text.Title(mark.Note, Limits.MarkNote, "mark note");
                if (marks.Any(m => m.Target == target)) throw SprawlException.Invalid($"Step {number}: '{mark.Target}' is marked twice.");
                marks.Add(new StepMark(target, markState, note));
            }
            steps.Add(new WalkthroughStep(text, ids, marks.Count == 0 ? null : marks));
        }
        var existing = state.Walkthroughs.Values.FirstOrDefault(w => w.RegionId == region.Id && Text.SameTitle(w.Title, title));
        if (existing is not null)
        {
            if (!add.Replace) throw new SprawlException(ErrorCodes.Duplicate, $"'{region.Title}' already has a walkthrough called '{existing.Title}' ({existing.Id}); pass replace to rewrite it.", 409) { Entities = [existing.Id] };
            state.Walkthroughs[existing.Id] = existing with { Title = title, Steps = steps, UpdatedAt = now };
            if (add.Ref is not null) { ValidateRef(add.Ref); MapRef(add.Ref, existing.Id); }
            return;
        }
        if (add.Ref is not null) ValidateRef(add.Ref);
        var newId = NewId(EntityKind.Walkthrough);
        state.Walkthroughs[newId] = new Walkthrough(newId, region.Id, title, steps, actor.Author, now, now);
        MapRef(add.Ref, newId);
    }

    private string? ResolveHighlight(string name, HashSet<string> scope)
    {
        if (name.Length == 0) return null;
        var id = refs.GetValueOrDefault(name) ?? name;
        if (state.Find(id) is { } found && EntityIds.KindOf(found.Id) is EntityKind.Card or EntityKind.Region or EntityKind.Connection) return found.Id;
        var card = state.Cards.Values.Where(c => c.RegionId is not null && scope.Contains(c.RegionId) && Text.SameTitle(c.Title, name))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
        if (card is not null) return card.Id;
        var region = state.Regions.Values.FirstOrDefault(r => scope.Contains(r.Id) && Text.SameTitle(r.Title, name));
        if (region is not null) return region.Id;
        // "d:Broker" from a diagram call whose author meant a title, not a node id: try the title after the prefix.
        var colon = name.IndexOf(':');
        return colon > 0 && colon < name.Length - 1 ? ResolveHighlight(name[(colon + 1)..], scope) : null;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    /// <summary>
    /// A card's box for where it is. Shapes take the size of their label inside a diagram and the column width elsewhere;
    /// plain cards keep their height class. A size the owner set by hand is kept.
    /// </summary>
    private Card Sized(Card card)
    {
        if (card.SizeSource == Placements.Manual) return card;
        var inDiagram = card.RegionId is { } r && state.Regions.TryGetValue(r, out var region) && RegionLayouts.IsDiagram(region.Layout);
        if (card.Kind is CardKinds.Mermaid or CardKinds.Table or CardKinds.Chart or CardKinds.Widget)
        {
            var (cw, ch) = card.Kind switch
            {
                CardKinds.Table => ContentGeometry.Table(card.Body),
                CardKinds.Chart => ContentGeometry.Chart(card.Body),
                CardKinds.Widget => ContentGeometry.Widget(card.Body),
                _ => MermaidGeometry.Size(card.Body)
            };
            if (inDiagram) return card with { Size = CardSizes.Tall, W = cw, H = ch };
            // In a card region it spans whole columns; its height follows the width it gets.
            var width = ContentGeometry.SpanWidth(cw);
            var height = (int)Math.Round(Math.Clamp(ch * Math.Min(1.0, (double)width / cw), Geometry.CompactHeight, 1600) / 8.0) * 8;
            return card with { Size = CardSizes.Tall, W = width, H = height };
        }
        if (card.Shape is { } shape)
        {
            var (w, h) = ShapeGeometry.Size(shape, card.Title, card.Icon);
            return inDiagram ? card with { Size = CardSizes.Compact, W = w, H = h }
                : card with { Size = CardSizes.Compact, W = Geometry.CardWidth, H = Math.Max(Geometry.CompactHeight, h) };
        }
        var size = Geometry.SizeFor(card.Kind, card.Body);
        return card with { Size = size, W = Geometry.CardWidth, H = Geometry.HeightOf(size) };
    }

    private void Disconnect(DisconnectIntent disconnect)
    {
        if (disconnect.Id is not null)
        {
            if (!state.Connections.Remove(disconnect.Id)) skipped.Add(new("missing", null, disconnect.Id));
            return;
        }
        if (disconnect.From is null || disconnect.To is null) throw SprawlException.Invalid("Give a connection id, or both from and to.");
        var from = ResolveEndpoint(disconnect.From);
        var to = ResolveEndpoint(disconnect.To);
        var kind = disconnect.Kind is null ? null : Text.OneOf(disconnect.Kind, ConnectionKinds.All, "connection kind");
        var matches = state.Connections.Values.Where(c => c.SourceId == from && c.TargetId == to && (kind is null || c.Kind == kind)).ToList();
        if (matches.Count == 0) skipped.Add(new("missing", null, $"{from}->{to}"));
        foreach (var connection in matches) state.Connections.Remove(connection.Id);
    }

    /// <summary>Point every connection at <paramref name="from"/> to <paramref name="to"/>, dropping self-loops and duplicates.</summary>
    private void Rewire(string from, string to)
    {
        foreach (var connection in state.Connections.Values.Where(c => c.SourceId == from || c.TargetId == from).OrderBy(c => c.Id, StringComparer.Ordinal).ToList())
        {
            var rewired = connection with
            {
                SourceId = connection.SourceId == from ? to : connection.SourceId,
                TargetId = connection.TargetId == from ? to : connection.TargetId
            };
            var duplicate = state.Connections.Values.Any(c => c.Id != connection.Id && c.SourceId == rewired.SourceId && c.TargetId == rewired.TargetId && c.Kind == rewired.Kind);
            if (rewired.SourceId == rewired.TargetId || duplicate) state.Connections.Remove(connection.Id);
            else state.Connections[connection.Id] = rewired;
        }
    }

    // ---- Canvas and owner layout ---------------------------------------------------------------------------------

    private void UpdateCanvas(UpdateCanvasIntent update)
    {
        var meta = state.Meta!;
        var tags = update.Tags is not null ? Text.Tags(update.Tags) : meta.Tags;
        if (update.AddTags is not null) tags = Text.Tags(tags.Concat(update.AddTags));
        if (update.RemoveTags is not null)
        {
            var removed = Text.Tags(update.RemoveTags).ToHashSet(StringComparer.Ordinal);
            tags = [.. tags.Where(tag => !removed.Contains(tag))];
        }
        if (update.ClearCollection && update.Collection is not null) throw SprawlException.Invalid("Give collection or clearCollection, not both.");
        state.Meta = meta with
        {
            Title = update.Title is null ? meta.Title : Text.Title(update.Title, Limits.CanvasTitle, "title"),
            Description = update.Description is null ? meta.Description : Text.Description(update.Description),
            Tags = tags,
            Collection = update.ClearCollection ? null : update.Collection is null ? meta.Collection : Text.Collection(update.Collection),
            Pinned = update.Pinned ?? meta.Pinned,
            Archived = update.Archived ?? meta.Archived
        };
    }

    private void SetLayout(SetLayoutIntent set)
    {
        if (!actor.IsOwner) throw SprawlException.Forbidden("Only the owner sets positions.");
        foreach (var item in set.Items)
        {
            switch (EntityIds.KindOf(item.Id))
            {
                case EntityKind.Card: SetCardLayout(RequireCard(item.Id), item); break;
                case EntityKind.Region: SetRegionLayout(RequireRegion(item.Id), item); break;
                case EntityKind.Sketch: SetSketchLayout(item); break;
                default: throw SprawlException.Invalid($"'{item.Id}' is not a card, region or sketch.");
            }
        }
    }

    private void SetCardLayout(Card card, LayoutItem item)
    {
        var regionId = item.Container switch
        {
            null => card.RegionId,
            "canvas" => null,
            var target => RequireRegion(target).Id
        };
        var (x, y) = Clamp(item.X, item.Y, regionId is not null);
        var updated = card with { RegionId = regionId, X = x, Y = y, Placement = item.AsAuto && regionId is not null ? Placements.Auto : Placements.Manual };
        if (item.W is not null || item.H is not null)
        {
            var width = (int)Math.Round(Math.Clamp(item.W ?? card.W, 120, 2400));
            var height = (int)Math.Round(Math.Clamp(item.H ?? card.H, 32, 2400));
            updated = updated with { W = width, H = height, SizeSource = Placements.Manual };
        }
        layout.Touch(card.Id);
        state.Cards[card.Id] = updated;
        layout.Touch(card.Id);
    }

    private void SetRegionLayout(Region region, LayoutItem item)
    {
        var parentId = item.Container switch
        {
            null => region.ParentId,
            "canvas" => null,
            var target => RequireRegion(target).Id
        };
        if (parentId != region.ParentId)
        {
            NestRegion(new NestRegionIntent(region.Id, parentId));
            region = state.Regions[region.Id];
        }
        var (x, y) = parentId is null ? ((int)Math.Round(item.X), (int)Math.Round(item.Y)) : Clamp(item.X, item.Y, true);
        layout.Touch(region.Id);
        state.Regions[region.Id] = region with { X = x, Y = y, Placement = item.AsAuto ? Placements.Auto : Placements.Manual };
        layout.Touch(region.Id);
    }

    private void AddSketch(AddSketchIntent add)
    {
        if (!actor.IsOwner) throw SprawlException.Forbidden("Only the owner draws.");
        var path = Text.SketchPath(add.Path);
        if (add.W is < 1 or > Limits.SketchSize || add.H is < 1 or > Limits.SketchSize) throw SprawlException.Invalid($"A sketch box is 1-{Limits.SketchSize} pixels on each side.");
        var region = add.Region is null ? null : RequireRegion(add.Region);
        var id = NewId(EntityKind.Sketch);
        if (add.Ref is not null) ValidateRef(add.Ref);
        MapRef(add.Ref, id);
        state.Sketches[id] = new Sketch(id, region?.Id, path, Text.Tint(add.Tint), Math.Clamp(add.Stroke ?? 2, 1, 24),
            (int)Math.Round(add.X), (int)Math.Round(add.Y), add.W, add.H, actor.Author, now);
    }

    private void DeleteSketches(DeleteSketchesIntent delete)
    {
        if (!actor.IsOwner) throw SprawlException.Forbidden("Only the owner erases sketches.");
        foreach (var id in delete.Ids)
            if (!state.Sketches.Remove(id)) skipped.Add(new("missing", null, id));
    }

    /// <summary>A dragged sketch: a new position in the same container, or into another region / onto the canvas.</summary>
    private void SetSketchLayout(LayoutItem item)
    {
        var sketch = state.Sketches.GetValueOrDefault(item.Id) ?? throw SprawlException.NotFound($"Sketch '{item.Id}' was not found.");
        var regionId = item.Container switch
        {
            null => sketch.RegionId,
            "canvas" => null,
            var target => RequireRegion(target).Id
        };
        state.Sketches[sketch.Id] = sketch with { RegionId = regionId, X = (int)Math.Round(item.X), Y = (int)Math.Round(item.Y) };
    }

    private void Unpin(UnpinIntent unpin)
    {
        if (!actor.IsOwner) throw SprawlException.Forbidden("Only the owner unpins items.");
        foreach (var id in unpin.Ids)
        {
            if (EntityIds.KindOf(id) == EntityKind.Region)
            {
                var region = RequireRegion(id);
                state.Regions[id] = region with { Placement = Placements.Auto };
                continue;
            }
            var card = RequireCard(id);
            if (card.RegionId is null) throw SprawlException.Invalid("A loose card has no region to be placed in; drag it into one.");
            layout.Touch(card.Id);
            state.Cards[card.Id] = Sized(card with { Placement = Placements.Auto, SizeSource = Placements.Auto });
            layout.PlaceCard(card.Id);
            layout.Touch(card.Id);
        }
    }

    // ---- Resolution helpers --------------------------------------------------------------------------------------

    private Region? ResolveRegion(string reference)
    {
        reference = reference.Trim();
        if (refs.TryGetValue(reference, out var mapped) && state.Regions.TryGetValue(mapped, out var byRef)) return byRef;
        if (state.Regions.TryGetValue(reference, out var byId)) return byId;
        var matches = state.Regions.Values.Where(region => Text.SameTitle(region.Title, reference))
            .OrderBy(region => region.ParentId is null ? 0 : 1).ThenBy(region => region.Id, StringComparer.Ordinal).ToList();
        if (matches.Count == 0) return null;
        var topLevel = matches.Where(region => region.ParentId is null).ToList();
        if (topLevel.Count == 1) return topLevel[0];
        if (matches.Count == 1) return matches[0];
        throw SprawlException.Invalid($"Region title '{reference}' is ambiguous; use one of: {string.Join(", ", matches.Select(region => region.Id))}.");
    }

    private Region RequireRegion(string reference) =>
        ResolveRegion(reference) ?? throw SprawlException.NotFound($"Region '{reference}' was not found.");

    private Card? ResolveCardOrNull(string reference)
    {
        var id = refs.GetValueOrDefault(reference) ?? reference;
        return state.Cards.GetValueOrDefault(id);
    }

    private Card RequireCard(string reference) =>
        ResolveCardOrNull(reference) ?? throw SprawlException.NotFound($"Card '{reference}' was not found.");

    private string ResolveEndpoint(string reference)
    {
        var id = refs.GetValueOrDefault(reference) ?? reference;
        if (state.Cards.ContainsKey(id) || state.Regions.ContainsKey(id)) return id;
        throw SprawlException.NotFound($"'{reference}' is not a card or region on this canvas (use a ref from this batch or an id).");
    }

    private static bool LooksLikeReference(string value) => EntityIds.IsWellFormed(value.Trim());

    private void MapRef(string? reference, string id)
    {
        if (reference is null) return;
        if (refs.TryGetValue(reference, out var existing) && existing != id) throw SprawlException.Invalid($"Ref '{reference}' is used twice in this batch.");
        refs[reference] = id;
    }

    private static void ValidateRef(string reference)
    {
        if (reference.Length is 0 or > Limits.Ref || EntityIds.IsWellFormed(reference))
            throw SprawlException.Invalid($"Ref '{reference}' must be 1-{Limits.Ref} characters and must not look like an id.");
    }

    private string NewId(EntityKind kind)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var id = ids.Next(kind);
            if (state.Find(id) is null && original.Find(id) is null && !refs.ContainsValue(id)) return id;
        }
        throw new InvalidOperationException("Could not allocate a unique id.");
    }

    private static (int X, int Y) Clamp(double x, double y, bool inRegion)
    {
        var (ix, iy) = ((int)Math.Round(x), (int)Math.Round(y));
        return inRegion ? (Math.Max(Geometry.Padding, ix), Math.Max(Geometry.Header, iy)) : (ix, iy);
    }
}

/// <summary>Input normalization and validation shared by every intent.</summary>
public static partial class Text
{
    public static string Title(string? value, int max, string label)
    {
        var title = Whitespace().Replace(value ?? "", " ").Trim();
        if (title.Length == 0) throw SprawlException.Invalid($"The {label} is required.");
        if (title.Length > max) throw SprawlException.Limit($"The {label} is at most {max} characters.");
        return title;
    }

    public static string Description(string? value)
    {
        var description = (value ?? "").Trim();
        return description.Length > Limits.CanvasDescription ? throw SprawlException.Limit($"The description is at most {Limits.CanvasDescription} characters.") : description;
    }

    public static string Body(string? value, string kind)
    {
        var body = (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var max = kind is CardKinds.Code or CardKinds.Mermaid or CardKinds.Table or CardKinds.Chart or CardKinds.Widget ? Limits.CodeBody : Limits.CardBody;
        if (body.Length > max) throw SprawlException.Limit($"A {kind} card body is at most {max} characters.");
        return kind == CardKinds.Chart ? ContentGeometry.ValidateChart(body) : body;
    }

    public static string? Url(string? value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
            return kind == CardKinds.Link ? throw SprawlException.Invalid("A link card needs a url.") : null;
        var url = value.Trim();
        if (url.Length > Limits.Url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw SprawlException.Invalid("The url must be an absolute http(s) URL without credentials.");
        return url;
    }

    public static string? Language(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var language = value.Trim().ToLowerInvariant();
        return language.Length > Limits.Language || !LanguageName().IsMatch(language) ? throw SprawlException.Invalid("The language must be a short name like 'csharp'.") : language;
    }

    public static string? Label(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var label = Whitespace().Replace(value, " ").Trim();
        return label.Length > Limits.Label ? throw SprawlException.Limit($"A connection label is at most {Limits.Label} characters.") : label;
    }

    public static string Kind(string value) => OneOf(value, CardKinds.All, "card kind");
    public static string? Shape(string? value) => value is null || value.Trim().ToLowerInvariant() == Shapes.None ? null : OneOf(value, Shapes.All, "shape");
    public static string? Icon(string? value) => value is null || value.Trim().ToLowerInvariant() == "none" ? null : OneOf(IconNames.Normalize(value), Icons.All, "icon");
    public static string? Layout(string? value) => value is null ? null : OneOf(value, RegionLayouts.All, "layout") is var l && l == RegionLayouts.Cards ? null : l;
    public static string? Line(string? value) => value is null ? null : OneOf(value, LineStyles.All, "line style") is var l && l == LineStyles.Solid ? null : l;
    public static string? Ends(string? value) => value is null ? null : OneOf(value, EndStyles.All, "ends");
    public static string Emphasis(string? value) => value is null ? Emphases.Normal : OneOf(value, Emphases.All, "emphasis");
    public static string? Tint(string? value) => value is null || value == Tints.None ? null : OneOf(value, Tints.All, "tint");

    public static string OneOf(string value, string[] allowed, string label)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return allowed.Contains(normalized, StringComparer.Ordinal) ? normalized : throw SprawlException.Invalid($"Unknown {label} '{value}'; use one of {string.Join(", ", allowed)}.");
    }

    public static string? Collection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Title(value, Limits.Collection, "collection");
    }

    public static IReadOnlyList<string> Tags(IEnumerable<string> values)
    {
        var tags = new List<string>();
        foreach (var value in values)
        {
            var tag = TagSeparators().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
            if (tag.Length == 0) continue;
            if (tag.Length > Limits.Tag || !TagName().IsMatch(tag)) throw SprawlException.Invalid($"Tag '{value}' must be letters, digits and dashes, at most {Limits.Tag} characters.");
            if (!tags.Contains(tag, StringComparer.Ordinal)) tags.Add(tag);
        }
        return tags.Count > Limits.Tags ? throw SprawlException.Limit($"A canvas has at most {Limits.Tags} tags.") : tags;
    }

    /// <summary>Titles compare case-insensitively, ignoring whitespace runs and trailing punctuation.</summary>
    public static bool SameTitle(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    /// <summary>SVG path data restricted to move, line, curve and close commands and numbers: nothing that can script or link.</summary>
    public static string SketchPath(string? value)
    {
        var path = (value ?? "").Trim();
        if (path.Length == 0 || path.Length > Limits.SketchPath) throw SprawlException.Invalid($"A sketch path is 1-{Limits.SketchPath} characters.");
        return SketchPathChars().IsMatch(path) && path[0] is 'M' or 'm' ? path : throw SprawlException.Invalid("A sketch path holds only M, L, Q, C and Z commands and numbers, starting with M.");
    }

    public static string Normalize(string title) => Whitespace().Replace(title.Trim().ToLowerInvariant(), " ").TrimEnd('.', '?', '!', ':', ';', ',');

    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex(@"^[MLQCZmlqcz0-9.,\s-]+$")] private static partial Regex SketchPathChars();
    [GeneratedRegex(@"[\s_]+")] private static partial Regex TagSeparators();
    [GeneratedRegex(@"^[\p{Ll}\p{Lo}\p{Nd}]+(?:-[\p{Ll}\p{Lo}\p{Nd}]+)*$")] private static partial Regex TagName();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9+#.-]*$")] private static partial Regex LanguageName();
}
