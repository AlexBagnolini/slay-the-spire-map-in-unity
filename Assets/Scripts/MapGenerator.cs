﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    public enum MapOrientation
    {
        BottomToTop,
        TopToBottom,
        RightToLeft,
        LeftToRight
    }

    public bool generateInStart = true;
    public MapConfig config;
    public MapOrientation orientation;
    public List<NodeBlueprint> randomNodes;
    public GameObject nodePrefab;
    [Header("Line settings")]
    public GameObject linePrefab;
    public int linePointsCount = 10;
    public float offsetFromNodes = 0.5f;
    
    private List<float> layerDistances;
    private GameObject mapParent;
    private List<List<Point>> paths;
    // ALL nodes by layer:
    private readonly List<List<MapNode>> nodes = new List<List<MapNode>>();

    private void Start()
    {
        if (generateInStart)
            Generate();
    }

    private void ClearMap()
    {
        if (mapParent != null)
            Destroy(mapParent);
        
        nodes.Clear();
    }

    public void Generate()
    {
        if (config == null)
        {
            Debug.LogWarning("Config was null in MapGenerator.Generate()");
            return;
        }

        ClearMap();
        
        mapParent = new GameObject("MapParent");
        
        GenerateLayerDistances();
        
        for (var i = 0; i < config.layers.Count; i++)
            PlaceLayer(i);
        
        GeneratePaths();
        
        RandomizeNodePositions();
        
        SetUpConnections();
        
        RemoveCrossConnections();
        
        SetOrientation();
        
        HideNodesWithoutConnectionsAndDrawLines();

        SetFirstLayerAttainable();
    }

    private void SetFirstLayerAttainable()
    {
        foreach (var node in nodes[0])
            node.SetState(NodeStates.Attainable);
    }

    private void SetOrientation()
    {
        switch (orientation)
        {
            case MapOrientation.BottomToTop:
                // do nothing
                break;
            case MapOrientation.TopToBottom:
                mapParent.transform.eulerAngles = new Vector3(0, 0, 180);
                break;
            case MapOrientation.RightToLeft:
                mapParent.transform.eulerAngles = new Vector3(0, 0, 90);
                break;
            case MapOrientation.LeftToRight:
                mapParent.transform.eulerAngles = new Vector3(0, 0, -90);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void HideNodesWithoutConnectionsAndDrawLines()
    {
        foreach (var node in nodes.SelectMany(layer => layer))
            if (node.HasNoConnections())
                node.gameObject.SetActive(false);
            else
            {
                // reset rotation to fix after orientation changes:
                node.transform.rotation = Quaternion.identity;
                foreach (var connection in node.OutgoingConnections)
                    AddLineConnection(node, connection);
            }
    }

    private void GenerateLayerDistances()
    {
        layerDistances = new List<float>();
        foreach (var layer in config.layers)
            layerDistances.Add(layer.distanceFromPreviousLayer.GetValue());
    }

    private float GetDistanceToLayer(int layerIndex)
    {
        if (layerIndex < 0 || layerIndex > layerDistances.Count) return 0f;
        
        return layerDistances.Take(layerIndex + 1).Sum();
    }

    private void PlaceLayer(int layerIndex)
    {
        var layer = config.layers[layerIndex];
        var layerParentObject = new GameObject("Layer " + layerIndex + " Parent");
        layerParentObject.transform.SetParent(mapParent.transform);
        var nodesOnThisLayer = new List<MapNode>();
        for (var i = 0; i < config.GridWidth; i++)
        {
            var nodeObject = Instantiate(nodePrefab, layerParentObject.transform);
            nodeObject.transform.localPosition = new Vector3(i * layer.nodesApartDistance, 0f, 0f);
            var node = nodeObject.GetComponent<MapNode>();
            nodesOnThisLayer.Add(node);
            var blueprint = UnityEngine.Random.Range(0f, 1f) < layer.randomizeNodes ? GetRandomNode() : layer.node;
            node.SetUp(blueprint, layerIndex);
        }

        nodes.Add(nodesOnThisLayer);
        // offset of this layer to make all the nodes centered:
        var offset = (nodesOnThisLayer[nodesOnThisLayer.Count - 1].transform.localPosition.x -
                      nodesOnThisLayer[0].transform.localPosition.x) / 2f;
        layerParentObject.transform.localPosition = new Vector3(- offset, GetDistanceToLayer(layerIndex), 0f);
    }

    private void RandomizeNodePositions()
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var list = nodes[index];
            var layer = config.layers[index];
            var distToNextLayer = index + 1 >= layerDistances.Count
                ? 0f
                : layerDistances[index + 1];
            var distToPreviousLayer = layerDistances[index];
            
            foreach (var node in list)
            {
                var xRnd = UnityEngine.Random.Range(-1f, 1f);
                var yRnd = UnityEngine.Random.Range(-1f, 1f);

                var x = xRnd * layer.nodesApartDistance / 2f;
                var y = yRnd < 0 ? distToPreviousLayer * yRnd / 2f : distToNextLayer * yRnd / 2f;

                node.transform.localPosition += new Vector3(x, y, 0) * layer.randomizePosition;
            }
        }
    }

    private void SetUpConnections()
    {
        foreach (var path in paths)
        {
            for (var i = 0; i < path.Count; i++)
            {
                var node = GetNode(path[i]);
                
                if (i > 0)
                {
                    // previous because the path is flipped
                    var nextNode = GetNode(path[i - 1]);
                    nextNode.AddIncoming(node);
                    node.AddOutgoing(nextNode);
                }

                if (i < path.Count - 1)
                {
                    var previousNode = GetNode(path[i + 1]);
                    previousNode.AddOutgoing(node);
                    node.AddIncoming(previousNode);
                }
            }
        }
    }

    private void RemoveCrossConnections()
    {
        for (var i = 0; i< config.GridWidth-1; i++)
        for (var j = 0; j < config.layers.Count-1; j++)
        {
            var node = GetNode(new Point(i, j));
            if (node == null || !node.gameObject.activeInHierarchy) continue;
            var right = GetNode(new Point(i + 1, j));
            if (right == null || !right.gameObject.activeInHierarchy) continue;
            var top = GetNode(new Point(i, j + 1));
            if (top == null || !top.gameObject.activeInHierarchy) continue;
            var topRight = GetNode(new Point(i + 1, j + 1));
            if (topRight == null || !topRight.gameObject.activeInHierarchy) continue;

            if (!node.OutgoingConnections.Contains(topRight))continue;
            if (!right.OutgoingConnections.Contains(top)) continue;

            // we managed to find a cross node:
            // 1) add direct connections:
            node.AddOutgoing(top);
            top.AddIncoming(node);
            
            right.AddOutgoing(topRight);
            topRight.AddIncoming(right);
            
            var rnd = UnityEngine.Random.Range(0f, 1f);
            if (rnd < 0.2f)
            {
                // remove both cross connections:
                // a) 
                node.RemoveOutgoing(topRight);
                topRight.RemoveIncoming(node);
                // b) 
                right.RemoveOutgoing(top);
                top.RemoveIncoming(right);
            }
            else if (rnd < 0.6f)
            {
                // a) 
                node.RemoveOutgoing(topRight);
                topRight.RemoveIncoming(node);
            }
            else
            {
                // b) 
                right.RemoveOutgoing(top);
                top.RemoveIncoming(right);
            }
        }
    }

    public void AddLineConnection(MapNode from, MapNode to)
    {
        var lineObject = Instantiate(linePrefab, mapParent.transform);
        var lineRenderer = lineObject.GetComponent<LineRenderer>();
        var fromPoint = from.transform.position +
                        (to.transform.position - from.transform.position).normalized * offsetFromNodes;

        var toPoint = to.transform.position +
                      (from.transform.position - to.transform.position).normalized * offsetFromNodes;

        // line renderer with 2 points only does not handle transparency properly:
        lineRenderer.positionCount = linePointsCount;
        for (var i = 0; i < linePointsCount; i++)
        {
            lineRenderer.SetPosition(i,
                Vector3.Lerp(fromPoint, toPoint, (float) i / (linePointsCount - 1)));
        }
        
        var dottedLine = lineObject.GetComponent<DottedLineRenderer>();
        if(dottedLine != null) dottedLine.ScaleMaterial();
    }

    private MapNode GetNode(Point p)
    {
        if (p.y >= nodes.Count) return null;
        if (p.x >= nodes[p.y].Count) return null;
        
        return nodes[p.y][p.x];
    }

    private Point GetFinalNode()
    {
        var y = config.layers.Count - 1;
        if (config.GridWidth % 2 == 1)
            return new Point(config.GridWidth / 2, y);

        return UnityEngine.Random.Range(0, 2) == 0
            ? new Point(config.GridWidth / 2, y)
            : new Point(config.GridWidth / 2 - 1, y);
    }

    private void GeneratePaths()
    {
        var finalNode = GetFinalNode();
        paths = new List<List<Point>>();
        var numOfStartingNodes = config.numOfStartingNodes.GetValue();
        var numOfPreBossNodes = config.numOfPreBossNodes.GetValue();

        var candidateXs = new List<int>();
        for (var i = 0; i < config.GridWidth; i++)
            candidateXs.Add(i);
        
        candidateXs.Shuffle();
        var preBossXs = candidateXs.Take(numOfPreBossNodes);
        var preBossPoints = (from x in preBossXs select new Point(x, finalNode.y - 1)).ToList();
        var attempts = 0;
        
        // start by generating paths from each of the preBossPoints to the 1st layer:
        foreach (var point in preBossPoints)
        {
            var path = Path(point, 0, config.GridWidth);
            path.Insert(0, finalNode);
            paths.Add(path);
            attempts++;
        }

        while (!PathsLeadToAtLeastNDifferentPoints(paths, numOfStartingNodes) && attempts < 100)
        {
            var randomPreBossPoint = preBossPoints[UnityEngine.Random.Range(0, preBossPoints.Count)];
            var path = Path(randomPreBossPoint, 0, config.GridWidth);
            path.Insert(0, finalNode);
            paths.Add(path);
            attempts++;
        }

        Debug.Log("Attempts to generate paths: " + attempts);
    }

    private bool PathsLeadToAtLeastNDifferentPoints(IEnumerable<List<Point>> paths, int n)
    {
        return (from path in paths select path[path.Count - 1].x).Distinct().Count() >= n;
    }

    private class Point: IEquatable<Point>
    {
        public int x; 
        public int y;

        public Point(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public bool Equals(Point other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return x == other.x && y == other.y;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Point) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (x * 397) ^ y;
            }
        }
    }

    private List<Point> Path(Point from, int toY, int width, bool firstStepUnconstrained = false)
    {
        if (from.y == toY)
        {
            Debug.LogError("Points are on same layers, return");
            return null;
        }
        
        // making one y step in this direction with each move
        var direction = from.y > toY ? -1 : 1;
        
        var path = new List<Point> {from};
        while (path[path.Count - 1].y != toY)
        {
            var lastPoint = path[path.Count - 1];
            var candidateXs = new List<int> ();
            if (firstStepUnconstrained && lastPoint.Equals(from))
            {
                for (var i = 0; i < width; i++)
                    candidateXs.Add(i);
            }
            else
            {
                // forward
                candidateXs.Add(lastPoint.x);
                // left
                if (lastPoint.x - 1 >= 0) candidateXs.Add(lastPoint.x - 1);
                // right
                if (lastPoint.x + 1 < width) candidateXs.Add(lastPoint.x + 1);
            }

            var nextPoint = new Point(candidateXs[UnityEngine.Random.Range(0, candidateXs.Count)], lastPoint.y + direction);
            path.Add(nextPoint);
        }

        return path;
    }

    private NodeBlueprint GetRandomNode()
    {
        return randomNodes.Count == 0 ? null : randomNodes[UnityEngine.Random.Range(0, randomNodes.Count)];
    }
}
