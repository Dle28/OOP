using System.Windows;
using System.Windows.Media;
using OsSim.Domain.ResourceAllocation;

namespace OsSim.Wpf.ViewModels;

/// <summary>
/// Builds a view-only resource allocation graph from a Banker snapshot.
/// Allocation edges are solid; remaining Need claims are dashed.
/// </summary>
public static class BankerRagProjector
{
    private const double CanvasWidth = 480;
    private const double ProcessLeft = 24;
    private const double ProcessSize = 64;
    private const double ResourceLeft = 332;
    private const double ResourceWidth = 124;
    private const double ResourceHeight = 58;
    private const double FirstNodeTop = 32;
    private const double RowSpacing = 68;

    public static BankerRagGraphVisual Project(BankerSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new BankerRagGraphVisual(
                CanvasWidth,
                280,
                "Các nút và cạnh RAG sẽ hiển thị từ snapshot Banker.",
                Array.Empty<BankerRagNodeVisual>(),
                Array.Empty<BankerGraphEdgeVisual>());
        }

        var rowCount = Math.Max(snapshot.Processes.Count, snapshot.ResourceNames.Count);
        var canvasHeight = Math.Max(280, FirstNodeTop + rowCount * RowSpacing + 20);
        return new BankerRagGraphVisual(
            CanvasWidth,
            canvasHeight,
            "Cạnh liền R → P: Allocation đang giữ. Cạnh nét đứt P → R: Need còn lại (claim tối đa, không phải request đang chờ).",
            BuildNodes(snapshot),
            BuildEdges(snapshot));
    }

    private static IReadOnlyList<BankerRagNodeVisual> BuildNodes(BankerSnapshot snapshot)
    {
        var nodes = new List<BankerRagNodeVisual>(snapshot.Processes.Count + snapshot.ResourceNames.Count);
        for (var processIndex = 0; processIndex < snapshot.Processes.Count; processIndex++)
        {
            var process = snapshot.Processes[processIndex];
            var need = FormatVector(snapshot.ResourceNames, process.Need);
            var allocation = FormatVector(snapshot.ResourceNames, process.Allocation);
            nodes.Add(new BankerRagNodeVisual(
                process.Id,
                process.IsFinished ? "DONE" : "ĐANG XÉT",
                $"{process.Id} · {(process.IsFinished ? "đã hoàn tất" : "chưa hoàn tất")}\nAllocation: {allocation}\nNeed: {need}",
                IsResource: false,
                IsFinished: process.IsFinished,
                Left: ProcessLeft,
                Top: ProcessTop(processIndex),
                Width: ProcessSize,
                Height: ProcessSize,
                PulseVersion: process.Id == snapshot.LastCompletedProcessId ? snapshot.StepNumber : -1));
        }

        for (var resourceIndex = 0; resourceIndex < snapshot.ResourceNames.Count; resourceIndex++)
        {
            var total = snapshot.Available[resourceIndex] + snapshot.Processes.Sum(process => process.Allocation[resourceIndex]);
            nodes.Add(new BankerRagNodeVisual(
                snapshot.ResourceNames[resourceIndex],
                $"Work {snapshot.Work[resourceIndex]} · Σ {total}",
                $"Tài nguyên {snapshot.ResourceNames[resourceIndex]} · Work hiện tại {snapshot.Work[resourceIndex]} / tổng {total}",
                IsResource: true,
                IsFinished: false,
                Left: ResourceLeft,
                Top: ResourceTop(resourceIndex),
                Width: ResourceWidth,
                Height: ResourceHeight,
                PulseVersion: -1));
        }

        return Array.AsReadOnly(nodes.ToArray());
    }

    private static IReadOnlyList<BankerGraphEdgeVisual> BuildEdges(BankerSnapshot snapshot)
    {
        var edges = new List<BankerGraphEdgeVisual>();
        for (var processIndex = 0; processIndex < snapshot.Processes.Count; processIndex++)
        {
            var process = snapshot.Processes[processIndex];
            if (process.IsFinished)
            {
                continue;
            }

            for (var resourceIndex = 0; resourceIndex < snapshot.ResourceNames.Count; resourceIndex++)
            {
                var allocated = process.Allocation[resourceIndex];
                var needed = process.Need[resourceIndex];
                var processY = ProcessTop(processIndex) + ProcessSize / 2;
                var resourceY = ResourceTop(resourceIndex) + ResourceHeight / 2;
                var parallelOffset = allocated > 0 && needed > 0 ? 6 : 0;

                if (allocated > 0)
                {
                    edges.Add(CreateEdge(
                        snapshot.ResourceNames[resourceIndex],
                        process.Id,
                        $"ALLOC {allocated}",
                        true,
                        ResourceLeft,
                        resourceY - parallelOffset,
                        ProcessLeft + ProcessSize,
                        processY - parallelOffset));
                }

                if (needed > 0)
                {
                    edges.Add(CreateEdge(
                        process.Id,
                        snapshot.ResourceNames[resourceIndex],
                        $"NEED {needed}",
                        false,
                        ProcessLeft + ProcessSize,
                        processY + parallelOffset,
                        ResourceLeft,
                        resourceY + parallelOffset));
                }
            }
        }

        return Array.AsReadOnly(edges.ToArray());
    }

    private static BankerGraphEdgeVisual CreateEdge(
        string fromLabel,
        string toLabel,
        string edgeLabel,
        bool isAllocation,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        const double arrowLength = 9;
        const double arrowHalfWidth = 4;
        var angle = Math.Atan2(endY - startY, endX - startX);
        var baseX = endX - arrowLength * Math.Cos(angle);
        var baseY = endY - arrowLength * Math.Sin(angle);
        var perpendicularX = arrowHalfWidth * Math.Sin(angle);
        var perpendicularY = -arrowHalfWidth * Math.Cos(angle);
        var arrowPoints = new PointCollection
        {
            new(endX, endY),
            new(baseX + perpendicularX, baseY + perpendicularY),
            new(baseX - perpendicularX, baseY - perpendicularY)
        };

        return new BankerGraphEdgeVisual(
            fromLabel,
            toLabel,
            edgeLabel,
            isAllocation,
            startX,
            startY,
            endX,
            endY,
            (startX + endX) / 2 - 26,
            (startY + endY) / 2 - 10,
            arrowPoints,
            $"{fromLabel} → {toLabel} · {edgeLabel}");
    }

    private static double ProcessTop(int index) => FirstNodeTop + index * RowSpacing;

    private static double ResourceTop(int index) => FirstNodeTop + 3 + index * RowSpacing;

    private static string FormatVector(IReadOnlyList<string> resourceNames, IReadOnlyList<int> values) =>
        string.Join(", ", values.Select((value, index) => $"{resourceNames[index]}:{value}"));
}

public sealed record BankerRagGraphVisual(
    double CanvasWidth,
    double CanvasHeight,
    string Summary,
    IReadOnlyList<BankerRagNodeVisual> Nodes,
    IReadOnlyList<BankerGraphEdgeVisual> Edges);

public sealed record BankerRagNodeVisual(
    string Label,
    string SubLabel,
    string Details,
    bool IsResource,
    bool IsFinished,
    double Left,
    double Top,
    double Width,
    double Height,
    long PulseVersion);

public sealed record BankerGraphEdgeVisual(
    string FromLabel,
    string ToLabel,
    string EdgeLabel,
    bool IsAllocation,
    double StartX,
    double StartY,
    double EndX,
    double EndY,
    double LabelX,
    double LabelY,
    PointCollection ArrowPoints,
    string Details);
