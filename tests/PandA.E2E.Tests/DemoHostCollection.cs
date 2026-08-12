namespace PandA.E2E.Tests;

/// <summary>Shares a single booted DemoHost + browser across all E2E tests.</summary>
[CollectionDefinition("e2e")]
public sealed class DemoHostCollection : ICollectionFixture<DemoHostFixture>;
