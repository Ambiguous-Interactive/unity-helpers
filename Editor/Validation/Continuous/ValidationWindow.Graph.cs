// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed partial class ValidationWindow
    {
        [SerializeField]
        private bool _graphMode;

        [SerializeField]
        private Vector2[] _graphPositions = new Vector2[3];
        private VisualElement _builderSurface;
        private VisualElement _targetNode;
        private VisualElement _checksNode;
        private VisualElement _reportNode;
        private readonly List<VisualElement> _conditionGraphNodes = new List<VisualElement>();
        private VisualElement _graphContent;
        private ValidationGraphConnections _graphConnections;
        private readonly List<VisualElement> _graphNodes = new List<VisualElement>();

        private VisualElement BuilderNode(string title, int index)
        {
            if (index == 0)
                _graphNodes.Clear();
            VisualElement node = Element(_graphContent, "sentinel-node");
            _graphNodes.Add(node);
            Label handle = AddLabel(node, title, "sentinel-node-title");
            handle.tooltip = "Drag this node in Graph mode. Form and Graph edit the same rule.";
            handle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (!_graphMode || evt.button != 0)
                    return;
                handle.CaptureMouse();
                evt.StopPropagation();
            });
            handle.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (!_graphMode || !handle.HasMouseCapture())
                    return;
                _graphPositions[index] += evt.mouseDelta;
                node.transform.position = _graphPositions[index];
                _graphConnections.MarkDirtyRepaint();
                evt.StopPropagation();
            });
            handle.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (handle.HasMouseCapture())
                    handle.ReleaseMouse();
            });
            node.RegisterCallback<GeometryChangedEvent>(_ => _graphConnections.MarkDirtyRepaint());
            return node;
        }

        private void SetBuilderMode(bool graph)
        {
            _graphMode = graph;
            if (_builderSurface == null || _checksNode == null)
                return;
            foreach (VisualElement node in _conditionGraphNodes)
            {
                _builderChecks.Add((VisualElement)node.userData);
                node.RemoveFromHierarchy();
            }
            _conditionGraphNodes.Clear();
            _graphNodes.Clear();
            _graphNodes.Add(_targetNode);
            _checksNode.EnableInClassList("dx-hidden", graph);
            if (graph)
            {
                int index = 0;
                while (0 < _builderChecks.childCount)
                {
                    VisualElement row = _builderChecks.ElementAt(0);
                    ValidationWorkspaceSettings.RuleCondition condition = _draft.checks[index];
                    VisualElement node = Element(_graphContent, "sentinel-node");
                    node.userData = row;
                    Label handle = AddLabel(node, "CHECK " + (index + 1), "sentinel-node-title");
                    node.Add(row);
                    node.transform.position = condition.graphPosition;
                    handle.RegisterCallback<MouseDownEvent>(evt =>
                    {
                        if (evt.button != 0)
                            return;
                        handle.CaptureMouse();
                        evt.StopPropagation();
                    });
                    handle.RegisterCallback<MouseMoveEvent>(evt =>
                    {
                        if (!handle.HasMouseCapture())
                            return;
                        condition.graphPosition += evt.mouseDelta;
                        node.transform.position = condition.graphPosition;
                        _graphConnections.MarkDirtyRepaint();
                        evt.StopPropagation();
                    });
                    handle.RegisterCallback<MouseUpEvent>(_ => handle.ReleaseMouse());
                    node.RegisterCallback<GeometryChangedEvent>(_ =>
                        _graphConnections.MarkDirtyRepaint()
                    );
                    _conditionGraphNodes.Add(node);
                    _graphNodes.Add(node);
                    index++;
                }
            }
            _graphContent.Add(_reportNode);
            _graphNodes.Add(_reportNode);
            _builderSurface.EnableInClassList("sentinel-graph", graph);
            _graphConnections.EnableInClassList("dx-hidden", !graph);
            _targetNode.transform.position = graph ? _graphPositions[0] : Vector2.zero;
            _reportNode.transform.position = graph ? _graphPositions[2] : Vector2.zero;
            _graphConnections.MarkDirtyRepaint();
        }

        private sealed class ValidationGraphConnections : VisualElement
        {
            private readonly List<VisualElement> _nodes;

            internal ValidationGraphConnections(List<VisualElement> nodes)
            {
                _nodes = nodes;
                pickingMode = PickingMode.Ignore;
                AddToClassList("sentinel-graph-connections");
                generateVisualContent += DrawConnections;
            }

            private void DrawConnections(MeshGenerationContext context)
            {
                for (int index = 1; index < _nodes.Count; index++)
                {
                    Rect first = _nodes[index - 1].worldBound;
                    Rect second = _nodes[index].worldBound;
                    Vector2 from = this.WorldToLocal(new Vector2(first.xMax, first.center.y));
                    Vector2 to = this.WorldToLocal(new Vector2(second.xMin, second.center.y));
                    Vector2 midpoint = new Vector2((from.x + to.x) * 0.5f, from.y);
                    Segment(context, from, midpoint);
                    Segment(context, midpoint, new Vector2(midpoint.x, to.y));
                    Segment(context, new Vector2(midpoint.x, to.y), to);
                }
            }

            private void Segment(MeshGenerationContext context, Vector2 from, Vector2 to)
            {
                Vector2 delta = to - from;
                if (delta.sqrMagnitude < 0.01f)
                    return;
                Vector2 normal = new Vector2(-delta.y, delta.x).normalized;
                MeshWriteData mesh = context.Allocate(4, 6);
                Color32 tint = resolvedStyle.color;
                mesh.SetNextVertex(new Vertex { position = from - normal, tint = tint });
                mesh.SetNextVertex(new Vertex { position = from + normal, tint = tint });
                mesh.SetNextVertex(new Vertex { position = to + normal, tint = tint });
                mesh.SetNextVertex(new Vertex { position = to - normal, tint = tint });
                mesh.SetNextIndex(0);
                mesh.SetNextIndex(1);
                mesh.SetNextIndex(2);
                mesh.SetNextIndex(2);
                mesh.SetNextIndex(3);
                mesh.SetNextIndex(0);
            }
        }
    }
#endif
}
