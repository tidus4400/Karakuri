using tidus4400.Karakuri.Shared;
using tidus4400.Karakuri.Orchestrator;

namespace tidus4400.Karakuri.Orchestrator.Tests;

public sealed class FlowOrderingTests
{
    [Fact]
    public void Order_WithEdges_ReturnsTopologicalOrder()
    {
        var definition = new FlowDefinition
        {
            Nodes =
            [
                new NodeDto { NodeId = "b" },
                new NodeDto { NodeId = "c" },
                new NodeDto { NodeId = "a" }
            ],
            Edges =
            [
                new EdgeDto { From = "a", To = "b" },
                new EdgeDto { From = "b", To = "c" }
            ]
        };

        var ordered = FlowOrdering.Order(definition);

        Assert.Equal(["a", "b", "c"], ordered.Select(x => x.NodeId).ToArray());
    }

    [Fact]
    public void Order_WithCycle_FallsBackToInputOrder()
    {
        var definition = new FlowDefinition
        {
            Nodes =
            [
                new NodeDto { NodeId = "x" },
                new NodeDto { NodeId = "y" }
            ],
            Edges =
            [
                new EdgeDto { From = "x", To = "y" },
                new EdgeDto { From = "y", To = "x" }
            ]
        };

        var ordered = FlowOrdering.Order(definition);

        Assert.Equal(["x", "y"], ordered.Select(x => x.NodeId).ToArray());
    }
}
