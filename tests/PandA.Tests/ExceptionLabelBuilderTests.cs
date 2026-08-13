using PandA.Core;
using PandA.Core.Labels;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

public sealed class ExceptionLabelBuilderTests
{
    private readonly ExceptionLabelBuilder _builder = new();
    private readonly InMemoryLabelTemplateRepository _repository = new();

    [Fact]
    public async Task Build_NoRead_ReplacesCartonIdAndHasNoLpnSlot()
    {
        var template = await _repository.FindActiveAsync(ExceptionLabelBuilder.TagFor(ExceptionType.NoRead)!);

        var result = _builder.Build(ExceptionType.NoRead, "C123", null, template);

        Assert.True(result.HasLabel);
        Assert.NotNull(result.Zpl);
        Assert.Contains("C123", result.Zpl);
        Assert.DoesNotContain("<CartonID>", result.Zpl);
        Assert.DoesNotContain("<LPN>", result.Zpl);
    }

    [Fact]
    public async Task Build_Duplicate_ReplacesCartonIdAndLpn()
    {
        var template = await _repository.FindActiveAsync(ExceptionLabelBuilder.TagFor(ExceptionType.Duplicate)!);

        var result = _builder.Build(ExceptionType.Duplicate, "C123", "LPN001", template);

        Assert.True(result.HasLabel);
        Assert.NotNull(result.Zpl);
        Assert.Contains("C123", result.Zpl);
        Assert.Contains("LPN001", result.Zpl);
        Assert.DoesNotContain("<CartonID>", result.Zpl);
        Assert.DoesNotContain("<LPN>", result.Zpl);
    }

    [Fact]
    public async Task Build_DataMismatch_DoesNotSubstituteLpn()
    {
        var template = await _repository.FindActiveAsync(ExceptionLabelBuilder.TagFor(ExceptionType.DataMismatch)!);

        var result = _builder.Build(ExceptionType.DataMismatch, "C123", "LPN001", template);

        Assert.True(result.HasLabel);
        Assert.NotNull(result.Zpl);
        Assert.Contains("DataMismatch", result.Zpl);
        Assert.DoesNotContain("LPN001", result.Zpl);
        Assert.DoesNotContain("<LPN>", result.Zpl);
    }

    [Fact]
    public async Task NotReceived_MapsToSeedTemplate()
    {
        var tag = ExceptionLabelBuilder.TagFor(ExceptionType.NotReceived);

        var template = await _repository.FindActiveAsync(tag!);

        Assert.Equal("Not Received", tag);
        Assert.NotNull(template);
        Assert.Equal("Not Received", template.LabelType);
        Assert.True(template.Active);
    }

    [Fact]
    public void Build_UnknownExceptionType_ReturnsNoTemplate()
    {
        var result = _builder.Build((ExceptionType)999, "C123", "LPN001", null);

        Assert.Null(ExceptionLabelBuilder.TagFor((ExceptionType)999));
        Assert.False(result.HasLabel);
        Assert.Null(result.Zpl);
        Assert.Null(result.Label);
    }

    [Fact]
    public async Task Build_SuccessResultHasExceptionIdentityAndLabel()
    {
        var template = await _repository.FindActiveAsync(ExceptionLabelBuilder.TagFor(ExceptionType.NoData)!);

        var result = _builder.Build(ExceptionType.NoData, "C123", null, template);

        Assert.True(result.HasLabel);
        Assert.Equal("Exception", result.LabelType);
        Assert.Equal("ExceptionLabel", result.Lpn);
        Assert.NotNull(result.Zpl);
        Assert.NotEmpty(result.Zpl);
        var label = Assert.IsType<Label>(result.Label);
        Assert.Equal("Exception", label.LabelType);
        Assert.Equal("ExceptionLabel", label.Lpn);
        Assert.Equal(result.Zpl, label.Zpl);
    }

    [Fact]
    public void Build_NullTemplate_ReturnsNoTemplateAndNoPrintableZpl()
    {
        var result = _builder.Build(ExceptionType.NoRead, "C123", null, null);

        Assert.False(result.HasLabel);
        Assert.Null(result.Zpl);
        Assert.Null(result.Label);
    }

    [Fact]
    public async Task Repository_DoesNotReturnInactiveTemplate()
    {
        var repository = new InMemoryLabelTemplateRepository(
        [
            new LabelTemplate("ScanError_NoRead", "^XA^FDInactive^FS^XZ", false)
        ]);

        var template = await repository.FindActiveAsync("ScanError_NoRead");

        Assert.Null(template);
    }
}
