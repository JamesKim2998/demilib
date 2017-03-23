﻿// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2017/03/11 20:31
// License Copyright (c) Daniele Giardini

using System;
using System.Collections.Generic;
using DG.DemiLib;
using UnityEditor;
using UnityEngine;

namespace DG.DemiEditor.DeGUINodeSystem
{
    /// <summary>
    /// Main class for DeGUI Node system.
    /// Create it, then enclose your GUI node calls inside a <see cref="DeGUINodeProcessScope"/>.<para/>
    /// CODING ORDER:<para/>
    /// - Create a <see cref="DeGUINodeProcess"/> to use for your node system (create it once, obviously)<para/>
    /// - Inside OnGUI, write all your nodes GUI code inside a <see cref="DeGUINodeProcessScope"/>
    /// </summary>
    public class DeGUINodeProcess
    {
        public EditorWindow editor { get; private set; }
        public DeGUINodeInteractionManager interactionManager { get; private set; }
        public DeGUINodeProcessSelection selection { get; private set; }
        public readonly DeGUINodeProcessOptions options = new DeGUINodeProcessOptions();
        public Rect area { get; private set; }
        public Vector2 areaShift { get; private set; }

        readonly Dictionary<Type,ABSDeGUINode> _typeToGUINode = new Dictionary<Type,ABSDeGUINode>();
        readonly Dictionary<IEditorGUINode,DeGUINodeData> _nodeToGUIData = new Dictionary<IEditorGUINode,DeGUINodeData>(); // Refilled on Layout event
        readonly Styles _styles = new Styles();
        bool _requiresRepaint; // Set to FALSE at each EndGUI

        #region CONSTRUCTOR

        /// <summary>
        /// Creates a new DeGUINodeProcess.
        /// </summary>
        /// <param name="editor">EditorWindow for this process</param>
        /// <param name="drawBackgroundGrid">If TRUE draws a background grid</param>
        /// <param name="forceDarkSkin">If TRUE forces the grid skin to black, otherwise varies based on current Unity free/pro skin</param>
        public DeGUINodeProcess(EditorWindow editor, bool drawBackgroundGrid = false, bool forceDarkSkin = false)
        {
            this.editor = editor;
            interactionManager = new DeGUINodeInteractionManager(this);
            selection = new DeGUINodeProcessSelection();
            options.drawBackgroundGrid = drawBackgroundGrid;
            options.forceDarkSkin = forceDarkSkin;
        }

        #endregion

        #region Public Methods

        /// <summary>Draws the given node using the given T editor GUINode type</summary>
        public void Draw<T>(IEditorGUINode node) where T : ABSDeGUINode, new()
        {
            ABSDeGUINode guiNode;
            Type type = typeof(T);
            if (!_typeToGUINode.ContainsKey(type)) {
                guiNode = new T { process = this };
                _typeToGUINode.Add(type, guiNode);
            } else guiNode = _typeToGUINode[type];
            Vector2 position = new Vector2((int)(node.guiPosition.x + areaShift.x), (int)(node.guiPosition.y + areaShift.y));
            DeGUINodeData guiNodeData = guiNode.GetAreas(position, node);

            // Draw node only if visible in area
            if (NodeIsVisible(guiNodeData.fullArea)) guiNode.OnGUI(guiNodeData, node);

            if (Event.current.type == EventType.Layout) _nodeToGUIData.Add(node, guiNodeData);
        }

        #endregion

        #region Internal Methods

        // Updates the main node process.
        // Sets <code>GUI.changed</code> to TRUE if the area is panned or a node is dragged.
        internal void BeginGUI(Rect nodeArea, ref Vector2 refAreaShift)
        {
            _styles.Init();
            area = nodeArea;
            areaShift = refAreaShift;

            // Determine mouse target type before clearing nodeGUIData dictionary
            if (!interactionManager.mouseTargetIsLocked) StoreMouseTarget();
            if (Event.current.type == EventType.Layout) _nodeToGUIData.Clear();

            // Update interactionManager
            if (interactionManager.Update()) _requiresRepaint = true;

            // Background grid
            if (options.drawBackgroundGrid) DeGUI.BackgroundGrid(area, areaShift, options.forceDarkSkin);

            // MOUSE EVENTS
            switch (Event.current.type) {
            case EventType.MouseDown:
                switch (Event.current.button) {
                case 0:
                    interactionManager.mousePositionOnLMBPress = Event.current.mousePosition;
                    switch (interactionManager.mouseTargetType) {
                    case DeGUINodeInteractionManager.TargetType.Background:
                        // LMB pressed on background
                        // Deselect all
                        if (!Event.current.shift && selection.DeselectAll()) _requiresRepaint = true;
                        // Start selection drawing
                        if (Event.current.shift) {
                            interactionManager.selectionMode = DeGUINodeInteractionManager.SelectionMode.Add;
                            selection.StoreSnapshot();
                        }
                        interactionManager.SetState(DeGUINodeInteractionManager.State.DrawingSelection);
                        break;
                    case DeGUINodeInteractionManager.TargetType.Node:
                        // LMB pressed on a node
                        // Select
                        bool isAlreadySelected = selection.IsSelected(interactionManager.targetNode);
                        if (Event.current.shift) {
                            if (isAlreadySelected) selection.Deselect(interactionManager.targetNode);
                            else selection.Select(interactionManager.targetNode, true);
                            _requiresRepaint = true;
                        } else if (!isAlreadySelected) {
                            selection.Select(interactionManager.targetNode, false);
                            _requiresRepaint = true;
                        }
                        //
                        if (interactionManager.nodeTargetType == DeGUINodeInteractionManager.NodeTargetType.DraggableArea) {
                            // LMB pressed on a node's draggable area: set state to draggingNodes
                            interactionManager.SetState(DeGUINodeInteractionManager.State.DraggingNodes);
                        }
                        break;
                    }
                    break;
                }
                break;
            case EventType.MouseDrag:
                switch (Event.current.button) {
                case 0:
                    switch (interactionManager.state) {
                    case DeGUINodeInteractionManager.State.DrawingSelection:
                        selection.selectionRect = new Rect(
                            Mathf.Min(interactionManager.mousePositionOnLMBPress.x, Event.current.mousePosition.x),
                            Mathf.Min(interactionManager.mousePositionOnLMBPress.y, Event.current.mousePosition.y),
                            Mathf.Abs(Event.current.mousePosition.x - interactionManager.mousePositionOnLMBPress.x),
                            Mathf.Abs(Event.current.mousePosition.y - interactionManager.mousePositionOnLMBPress.y)
                        );
                        if (interactionManager.selectionMode == DeGUINodeInteractionManager.SelectionMode.Add) {
                            // Add eventual nodes stored when starting to draw
                            selection.Select(selection.selectedNodesSnapshot, false);
                        } else selection.DeselectAll();
                        foreach (KeyValuePair<IEditorGUINode, DeGUINodeData> kvp in _nodeToGUIData) {
                            if (selection.selectionRect.Includes(kvp.Value.fullArea)) selection.Select(kvp.Key, true);
                        }
                        _requiresRepaint = true;
                        break;
                    case DeGUINodeInteractionManager.State.DraggingNodes:
                        // Drag node/s
                        foreach (IEditorGUINode node in selection.selectedNodes) node.guiPosition += Event.current.delta;
                        GUI.changed = _requiresRepaint = true;
                        break;
                    }
                    break;
                case 2:
                    // Panning
                    interactionManager.SetState(DeGUINodeInteractionManager.State.Panning);
                    refAreaShift = areaShift += Event.current.delta;
                    GUI.changed = _requiresRepaint = true;
                    break;
                }
                break;
            case EventType.MouseUp:
                switch (interactionManager.state) {
                case DeGUINodeInteractionManager.State.DrawingSelection:
                    interactionManager.selectionMode = DeGUINodeInteractionManager.SelectionMode.Default;
                    selection.ClearSnapshot();
                    selection.selectionRect = new Rect();
                    _requiresRepaint = true;
                    break;
                }
                interactionManager.SetState(DeGUINodeInteractionManager.State.Inactive);
                break;
            case EventType.ContextClick:
                break;
            }
        }

        internal void EndGUI()
        {
            // EVIDENCE SELECTED NODES + DRAW RECTANGULAR SELECTION
            if (Event.current.type == EventType.Repaint) {
                // Evidence selected nodes
                if (options.evidenceSelectedNodes && selection.selectedNodes.Count > 0) {
                    using (new DeGUI.ColorScope(options.evidenceSelectedNodesColor)) {
                        foreach (IEditorGUINode node in selection.selectedNodes) {
                            DeGUINodeData data = _nodeToGUIData[node];
                            GUI.Box(data.fullArea.Expand(3), "", _styles.nodeOutlineThick);
                        }
                    }
                }
                // Draw selection
                if (interactionManager.state == DeGUINodeInteractionManager.State.DrawingSelection) {
                    using (new DeGUI.ColorScope(options.evidenceSelectedNodesColor)) {
                        GUI.Box(selection.selectionRect, "", _styles.selectionRect);
                    }
                }
            }

            // Repaint if necessary
            if (_requiresRepaint) {
                editor.Repaint();
                _requiresRepaint = false;
            }
        }

        #endregion

        #region Methods

        // Store mouse target (even in case of rollovers)
        void StoreMouseTarget()
        {
            if (!area.Contains(Event.current.mousePosition)) {
                // Mouse out of editor
                interactionManager.SetMouseTargetType(DeGUINodeInteractionManager.TargetType.None);
                interactionManager.targetNode = null;
                return;
            }
            foreach (KeyValuePair<IEditorGUINode, DeGUINodeData> kvp in _nodeToGUIData) {
                if (kvp.Value.fullArea.Contains(Event.current.mousePosition)) {
                    // Mouse on node
                    interactionManager.targetNode = kvp.Key;
                    if (_nodeToGUIData[kvp.Key].dragArea.Contains(Event.current.mousePosition)) {
                        // Mouse on node's drag area
                        interactionManager.SetMouseTargetType(DeGUINodeInteractionManager.TargetType.Node, DeGUINodeInteractionManager.NodeTargetType.DraggableArea);
                    } else {
                        // Mouse on node but outside drag area
                        interactionManager.SetMouseTargetType(DeGUINodeInteractionManager.TargetType.Node, DeGUINodeInteractionManager.NodeTargetType.NonDraggableArea);
                    }
                    return;
                }
            }
            interactionManager.SetMouseTargetType(DeGUINodeInteractionManager.TargetType.Background);
            interactionManager.targetNode = null;
        }

        #endregion

        #region Helpers

        bool NodeIsVisible(Rect nodeArea)
        {
            return nodeArea.xMax > area.xMin && nodeArea.xMin < area.xMax && nodeArea.yMax > area.yMin && nodeArea.yMin < area.yMax;
        }

        #endregion

        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████
        // ███ INTERNAL CLASSES ████████████████████████████████████████████████████████████████████████████████████████████████
        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████

        class Styles
        {
            public GUIStyle selectionRect, nodeOutline, nodeOutlineThick;
            bool _initialized;

            public void Init()
            {
                if (_initialized) return;

                _initialized = true;
                selectionRect = DeGUI.styles.box.flat.Clone().Background(DeStylePalette.squareBorderAlpha15);
                nodeOutline = DeGUI.styles.box.flat.Clone().Background(DeStylePalette.squareBorderEmpty);
                nodeOutlineThick = nodeOutline.Clone().Border(new RectOffset(5, 5, 5, 5)).Background(DeStylePalette.squareBorderThickEmpty);
            }
        }
    }
}