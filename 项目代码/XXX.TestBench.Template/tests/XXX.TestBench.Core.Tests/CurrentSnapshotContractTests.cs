using System.Text.Json;
using System.Text.Json.Nodes;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Domain.TestPoints;

namespace XXX.TestBench.Core.Tests;

public sealed class CurrentSnapshotContractTests
{
    [Fact]
    public void ReportInput_RoundTripsAndRejectsMissingUnknownVersionAndNullMetadata()
    {
        var runId = Guid.NewGuid().ToString("D");
        var snapshot = new ReportInputSnapshotV1
        {
            DatabaseInstanceId = Guid.NewGuid().ToString("D"), RunId = runId, ResultContractVersion = 2,
            Purpose = ReportPurpose.ProductEvaluation, Scope = RunScope.Single, ExecutionState = RecordState.Completed,
            Metadata = new ReportInputMetadata
            {
                RecordNumber = "R-sample", RunId = runId, ProductModelName = "样例型号", OperatorName = "操作员",
                StartedAtUtc = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 11, 13, 0, 1, 0, DateTimeKind.Utc),
                Scope = RunScope.Single, ExecutionState = RecordState.Completed
            },
            Parameters = [], DeviceModes = [], TargetTimeZoneId = "Asia/Shanghai",
            Items = [new ReportInputTestItem { PointId = 1, Name = "样例项点", ExecutorCode = "sample", OutputContractVersion = 1, State = ItemResultState.Succeeded, Measurements = [] }],
            Resources = new ReportInputResources { TemplateKey = "sample", MappingKey = "sample", TemplateHash = new string('a', 64), MappingHash = new string('b', 64) }
        };
        var serialized = ReportInputSnapshotSerializer.Serialize(snapshot);
        Assert.Equal(runId, ReportInputSnapshotSerializer.Deserialize(serialized.JsonText).RunId);
        Assert.Equal(serialized.InputHash, ReportInputSnapshotSerializer.ComputeHash(serialized.JsonText));
        Assert.NotEqual(serialized.InputHash, ReportInputSnapshotSerializer.ComputeHash(serialized.JsonText + " "));
        var json = JsonNode.Parse(serialized.JsonText)!.AsObject();
        json.Remove("schemaVersion");
        Assert.Throws<JsonException>(() => ReportInputSnapshotSerializer.Deserialize(json.ToJsonString()));
        json["schemaVersion"] = 99;
        Assert.Throws<InvalidDataException>(() => ReportInputSnapshotSerializer.Deserialize(json.ToJsonString()));
        json["schemaVersion"] = 1;
        json["metadata"] = null;
        Assert.Throws<InvalidDataException>(() => ReportInputSnapshotSerializer.Deserialize(json.ToJsonString()));
    }

    [Fact]
    public void Sequence_RoundTrip_PreservesOrderAndVersions()
    {
        SequenceItem[] items = [new(7, "项点七", "executor", "Number", 2, 12, 3), new(4, "项点四", "executor", "Number", 1, 5, 2)];
        Assert.Equal(items, SequenceSnapshot.FromJson(SequenceSnapshot.ToJson(items)));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"Items\":[]}")]
    [InlineData("{\"SchemaVersion\":99,\"Items\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Items\":[null]}")]
    [InlineData("{\"SchemaVersion\":1,\"Items\":[],\"extra\":1}")]
    public void Sequence_RejectsLegacyUnknownAndMalformed(string json)
        => Assert.Null(SequenceSnapshot.FromJson(json));

    [Fact]
    public void Sequence_RejectsDuplicateIdentity()
    {
        var item = new SequenceItem(1, "项点", "executor", "Number", 1);
        Assert.Throws<ArgumentException>(() => SequenceSnapshot.ToJson(new[] { item, item }));
    }

    [Theory]
    [InlineData("{\"ProductTypeId\":1,\"ProductModelId\":2,\"TestTimeSeconds\":60,\"TestVoltageV\":100,\"ProtectCurrentMa\":1}")]
    [InlineData("{\"SchemaVersion\":99}")]
    public void Parameters_RejectLegacyAndUnknownVersion(string json)
        => Assert.Null(TestParameterSnapshot.FromJson(json));

    [Fact]
    public void Measurements_RejectMissingAndUnknownVersionAndNullElements()
    {
        Assert.Throws<JsonException>(() => MeasuredValuesSnapshotSerializer.Deserialize("{\"values\":[]}"));
        Assert.Throws<InvalidDataException>(() => MeasuredValuesSnapshotSerializer.Deserialize("{\"schemaVersion\":99,\"values\":[]}"));
        Assert.Throws<InvalidDataException>(() => MeasuredValuesSnapshotSerializer.Deserialize("{\"schemaVersion\":1,\"values\":[null]}"));
        Assert.Empty(MeasuredValuesSnapshotSerializer.Deserialize(MeasuredValuesSnapshotSerializer.Serialize(Array.Empty<MeasuredValue>()).JsonText));
    }
}
