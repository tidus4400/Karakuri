using tidus4400.Karakuri.Shared;

namespace tidus4400.Karakuri.Runner;

internal static class RunnerFlowOrdering
{
    public static List<NodeDto> Order(FlowDefinition definition)
    {
        if (definition.Edges.Count == 0)
        {
            return definition.Nodes.ToList();
        }

        var nodes = definition.Nodes.ToDictionary(n => n.NodeId, n => n);
        var indegree = definition.Nodes.ToDictionary(n => n.NodeId, _ => 0);
        var outgoing = definition.Nodes.ToDictionary(n => n.NodeId, _ => new List<string>());

        foreach (var edge in definition.Edges)
        {
            if (!nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To))
            {
                continue;
            }

            outgoing[edge.From].Add(edge.To);
            indegree[edge.To]++;
        }

        var queue = new Queue<string>(indegree.Where(x => x.Value == 0).Select(x => x.Key));
        var result = new List<NodeDto>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            result.Add(nodes[id]);
            foreach (var next in outgoing[id])
            {
                indegree[next]--;
                if (indegree[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        return result.Count == definition.Nodes.Count ? result : definition.Nodes.ToList();
    }
}
