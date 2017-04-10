﻿// Author: Daniele Giardini - http://www.demigiant.com
// Created: 2017/03/13 14:33
// License Copyright (c) Daniele Giardini

using DG.DemiLib;
using UnityEditor;
using UnityEngine;

namespace DG.DemiEditor.DeGUINodeSystem
{
    /// <summary>
    /// One per <see cref="NodeProcess"/>.
    /// Partially independent, mainly controlled by process.
    /// </summary>
    public class InteractionManager
    {
        public enum State
        {
            Inactive,
            Panning,
            DrawingSelection,
            DraggingNodes,
            DraggingConnector,
            ContextClick,
            DoubleClick // Only if valid (meaning on same target it started)
        }

        // Recorded on mouse down, indicates how the state will change if the user drags the mouse instead of releasing it
        public enum ReadyFor
        {
            Unset,
            Panning,
            DrawingSelection,
            DraggingNodes,
            DraggingConnector
        }

        public enum TargetType
        {
            None,
            Background,
            Node
        }

        public enum NodeTargetType
        {
            None,
            DraggableArea,
            NonDraggableArea
        }

        public State state { get; private set; }
        public ReadyFor readyForState { get; private set; }
        public TargetType mouseTargetType { get; private set; } // Always updated, even on rollover
        public NodeTargetType nodeTargetType { get; private set; }
        public IEditorGUINode targetNode { get; internal set; }
        public Vector2 mousePositionOnLMBPress { get; internal set; } // Stored mouse position last time LMB was pressed
        public bool controlKey { get; private set; } // TRUE when the CTRL key must be considered for key combinations (so it's valid until all keys are released)
        public bool mouseTargetIsLocked {
            get {
                switch (state) {
                    case State.DraggingNodes: case State.DraggingConnector: case State.Panning: return true;
                }
                switch (readyForState) {
                    case ReadyFor.DraggingNodes: case ReadyFor.DraggingConnector: case ReadyFor.Panning: return true;
                }
                return false;
            }
        }

        const float _DoubleClickTime = 0.4f;
        internal static readonly float MinDragStartupDistance = 10; // Min drag pixels required to actually start some drag operations
        readonly NodeProcess _process;
        MouseCursor _currMouseCursor;
        MouseSnapshot _lastLMBUpSnapshot;
        float _timeAtControlKeyRelease;

        #region CONSTRUCTOR

        public InteractionManager(NodeProcess process)
        {
            _process = process;
        }

        #endregion

        #region Public Methods

        /// <summary>Returns TRUE if the given node is currently being dragged</summary>
        public bool IsDragging(IEditorGUINode node)
        {
            return state == State.DraggingNodes && targetNode == node;
        }

        /// <summary>
        /// Returns TRUE if the control key should be considered valid for this operation
        /// (returns TRUE even if it was released within a given timespan)
        /// </summary>
        public bool HasControlKeyModifier()
        {
            return Event.current.control || Time.realtimeSinceStartup - _timeAtControlKeyRelease < 0.2f;
        }

        #endregion

        #region Internal Methods

        // Also resets readyForState
        internal void SetState(State toState)
        {
            State prevState = state;
            state = toState;
            readyForState = ReadyFor.Unset;

            // Repaint editor if necessary
            switch (prevState) {
            case State.Panning:
            case State.DraggingNodes:
                _process.editor.Repaint();
                break;
            }
        }

        internal void SetReadyFor(ReadyFor value)
        {
            readyForState = value;
        }

        internal void SetMouseTargetType(TargetType targetType, NodeTargetType nodeTargetType = NodeTargetType.None)
        {
            this.mouseTargetType = targetType;
            this.nodeTargetType = nodeTargetType;
        }

        // Used to evaluate correct state on mouseUp and eventual LMB double-click.
        // Returns TRUE if a valid double-click happened.
        internal bool EvaluateMouseUp()
        {
            if (Event.current.button != 0) {
                // Right or Middle mouse button pressed: reset double-click counter
                _lastLMBUpSnapshot.Reset();
                return false;
            }
            bool isDoubleClick = _lastLMBUpSnapshot.time > 0f
                                 && Time.realtimeSinceStartup - _lastLMBUpSnapshot.time <= _DoubleClickTime
                                 && _lastLMBUpSnapshot.mouseTargetType == mouseTargetType
                                 && (mouseTargetType != TargetType.Node || targetNode.id == _lastLMBUpSnapshot.targetNodeId);
            if (isDoubleClick) {
                _lastLMBUpSnapshot.Reset();
                return true;
            }
            // First click
            _lastLMBUpSnapshot = new MouseSnapshot(Time.realtimeSinceStartup, mouseTargetType, targetNode == null ? null : targetNode.id);
            return false;
        }

        /// <summary>
        /// Returns TRUE if a repaint is required
        /// </summary>
        /// <returns></returns>
        internal bool Update()
        {
            // Evaluate control key
            if (Event.current.type == EventType.KeyUp && (Event.current.keyCode == KeyCode.LeftControl || Event.current.keyCode == KeyCode.RightControl)) {
                _timeAtControlKeyRelease = Time.realtimeSinceStartup;
            }

            // Evaluate mouse cursor
            MouseCursor prevMouseCursor = _currMouseCursor;
            switch (state) {
            case State.Panning:
                _currMouseCursor = MouseCursor.Pan;
                break;
            case State.DrawingSelection:
                switch (_process.selection.selectionMode) {
                case SelectionManager.Mode.Add:
                    _currMouseCursor = MouseCursor.ArrowPlus;
                    break;
                case SelectionManager.Mode.Subtract:
                    _currMouseCursor = MouseCursor.ArrowMinus;
                    break;
                }
                break;
            case State.DraggingNodes:
                _currMouseCursor = MouseCursor.MoveArrow;
                break;
            default:
                _currMouseCursor = MouseCursor.Arrow;
                break;
            }
            if (_currMouseCursor != MouseCursor.Arrow) EditorGUIUtility.AddCursorRect(_process.area, _currMouseCursor);
            return _currMouseCursor != prevMouseCursor;
        }

        #endregion

        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████
        // ███ INTERNAL CLASSES ████████████████████████████████████████████████████████████████████████████████████████████████
        // █████████████████████████████████████████████████████████████████████████████████████████████████████████████████████

        struct MouseSnapshot
        {
            public float time;
            public TargetType mouseTargetType;
            public string targetNodeId;

            public MouseSnapshot(float time, TargetType mouseTargetType, string targetNodeId)
            {
                this.time = time;
                this.mouseTargetType = mouseTargetType;
                this.targetNodeId = targetNodeId;
            }

            public void Reset()
            {
                time = 0;
                mouseTargetType = TargetType.None;
                targetNodeId = null;
            }
        }
    }
}