/// <summary>
/// Provides the FullScreenView class for rendering borderless full-screen game views.
///
/// Renders a camera to a borderless popup editor window, enabling multi-monitor VR
/// display setups within the Unity editor.
/// </summary>
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Renders a borderless full-screen game view in an editor window.
    /// </summary>
    public class FullScreenView : EditorWindow
    {
        /// <summary>The list of all active full-screen views.</summary>
        public static readonly List<FullScreenView> Views = new List<FullScreenView>();

        /// <summary>The entity ID of the camera to render.</summary>
        public EntityId cameraEntityId;

        /// <summary>The camera component for rendering.</summary>
        private Camera _camera;

        /// <summary>Determines whether the view is currently rendering.</summary>
        private bool _rendering = false;

        /// <summary>Adds this view to the views list and registers the quit and play-mode handlers.</summary>
        /// <remarks>
        /// Registration lives here rather than in Awake because a domain reload rebuilds <see cref="Views"/>
        /// empty without re-invoking Awake on a window that survived it, which would orphan every open view.
        /// The play-mode subscription closes the view when Play Mode ends. Without it the borderless
        /// window outlives the scene restore that Unity performs on exit, which clears the camera's
        /// programmatically assigned <c>targetTexture</c> and leaves the OnGUI render path drawing
        /// against a null texture (visible as a stale afterimage plus a console error).
        /// </remarks>
        private void OnEnable()
        {
            if (!Views.Contains(this))
            {
                Views.Add(this);
            }

            EditorApplication.wantsToQuit -= OnEditorWantsToQuit;
            EditorApplication.wantsToQuit += OnEditorWantsToQuit;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Unregisters the quit and play-mode handlers when disabled.</summary>
        private void OnDisable()
        {
            EditorApplication.wantsToQuit -= OnEditorWantsToQuit;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        /// <summary>Closes the view when Play Mode ends so the post-restore null targetTexture cannot fire.</summary>
        /// <param name="state">The current Play Mode transition.</param>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Close();
            }
        }

        /// <summary>Handles GUI events and renders the camera view.</summary>
        private void OnGUI()
        {
            Event currentEvent = Event.current;
            if (currentEvent.isMouse && currentEvent.button == 0 && !EditorApplication.isPlaying)
            {
                Close();
            }
            else if (currentEvent.type == EventType.Repaint)
            {
                if (_camera == null)
                {
                    _camera = (Camera)EditorUtility.EntityIdToObject(cameraEntityId);
                    if (_camera != null)
                    {
                        _camera.enabled = false;

                        // A domain reload clears the private fields while the camera keeps the texture assigned
                        // to it, so the surviving texture is freed before a replacement takes its place.
                        if (_camera.targetTexture != null)
                        {
                            _camera.targetTexture.Release();
                            UnityEngine.Object.DestroyImmediate(_camera.targetTexture);
                        }

                        int renderWidth = (int)position.width;
                        int renderHeight = (int)position.height;
                        _camera.targetTexture = new RenderTexture(
                            renderWidth,
                            renderHeight,
                            24,
                            RenderTextureFormat.ARGB32
                        );
                        _rendering = true;
                    }
                }
                if (_rendering && _camera != null && _camera.targetTexture != null)
                {
                    _camera.Render();
                    GUI.DrawTexture(
                        new Rect(0, 0, position.width, position.height),
                        _camera.targetTexture,
                        ScaleMode.ScaleToFit,
                        alphaBlend: false
                    );
                }
            }
        }

        /// <summary>Triggers repaint each frame when rendering.</summary>
        private void Update()
        {
            if ((_camera != null) && _rendering)
            {
                Repaint();
            }
        }

        /// <summary>Cleans up camera resources when destroyed.</summary>
        private void OnDestroy()
        {
            _rendering = false;
            if (_camera != null)
            {
                if (_camera.targetTexture != null)
                {
                    _camera.targetTexture.Release();
                    _camera.targetTexture = null;
                }
                _camera.enabled = true;
            }
            Views.Remove(this);
        }

        /// <summary>Closes this view when the editor is quitting.</summary>
        /// <returns>Always returns true to allow the editor to quit.</returns>
        private bool OnEditorWantsToQuit()
        {
            Close();
            return true;
        }
    }
}
#endif
