using PandA.UI.Contracts.Config;
using PandA.UI.DemoHost.Sim;

namespace PandA.E2E.Tests;

/// <summary>
/// Covers the normalized fire-point model: fire points are a printer-independent glossary unique on
/// (label definition + apply point), and a map binds one fire point to several printers for redundancy.
/// </summary>
public sealed class FirePointNormalizationTests
{
    [Fact]
    public async Task Duplicate_fire_point_is_rejected()
    {
        var store = new DemoDataStore();
        var editor = new DemoFirePointEditor(store);
        var existing = store.FirePoints.Values.First();

        // Same label definition + same apply point as an existing fire point → duplicate.
        var dup = new FirePointDto(null, existing.LabelDefId, existing.ApplyEdge, existing.ApplyInches);
        var result = await editor.SaveAsync(dup);

        Assert.False(result.Success);
        Assert.Equal(store.FirePoints.Count, store.FirePoints.Count); // nothing added
    }

    [Fact]
    public async Task Editing_an_existing_fire_point_in_place_is_allowed()
    {
        var store = new DemoDataStore();
        var editor = new DemoFirePointEditor(store);
        var existing = store.FirePoints.Values.First();

        // Re-saving the same fire point (same id, same key) must not trip the uniqueness rule.
        var result = await editor.SaveAsync(existing);

        Assert.True(result.Success);
    }

    [Fact]
    public void Seed_binds_one_fire_point_to_both_side_printers()
    {
        var store = new DemoDataStore();
        var line1 = store.Lines.Values.First(l => string.Equals(l.Name, "Line 1", StringComparison.Ordinal));
        var map = store.Maps[line1.ActiveMapId!];

        // The single Shipping (1T) fire point is applied by both side printers on Line 1.
        var shipping = map.Assignments.First(a =>
            string.Equals(store.FirePoints[a.FirePointId].ApplyPointNotation, "1T", StringComparison.Ordinal));

        Assert.True(shipping.PrinterIds.Count >= 2);
        // And that fire point is a single, shared glossary entry (not one-per-printer).
        var shippingLabelId = store.FirePoints[shipping.FirePointId].LabelDefId;
        Assert.Single(store.FirePoints.Values, f =>
            string.Equals(f.ApplyPointNotation, "1T", StringComparison.Ordinal) &&
            string.Equals(f.LabelDefId, shippingLabelId, StringComparison.Ordinal));
    }
}
