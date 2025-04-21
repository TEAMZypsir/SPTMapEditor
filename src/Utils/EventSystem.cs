using System;
using System.Collections.Generic;
using UnityEngine;

namespace TransformCacher
{
    /// <summary>
    /// Simple event system for cross-component communication
    /// </summary>
    public static class EventSystem
    {
        // Dictionary to store event handlers
        private static Dictionary<string, List<Action<object>>> _eventHandlers = new Dictionary<string, List<Action<object>>>();

        /// <summary>
        /// Register a handler for a specific event
        /// </summary>
        public static void RegisterEvent(string eventName, Action<object> handler)
        {
            if (!_eventHandlers.ContainsKey(eventName))
            {
                _eventHandlers[eventName] = new List<Action<object>>();
            }
            
            _eventHandlers[eventName].Add(handler);
        }

        /// <summary>
        /// Unregister a handler from a specific event
        /// </summary>
        public static void UnregisterEvent(string eventName, Action<object> handler)
        {
            if (_eventHandlers.ContainsKey(eventName))
            {
                _eventHandlers[eventName].Remove(handler);
            }
        }

        /// <summary>
        /// Trigger an event with data
        /// </summary>
        public static void TriggerEvent(string eventName, object data = null)
        {
            if (_eventHandlers.ContainsKey(eventName))
            {
                foreach (var handler in _eventHandlers[eventName])
                {
                    try
                    {
                        handler?.Invoke(data);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error in event handler for {eventName}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Clear all registered events
        /// </summary>
        public static void ClearEvents()
        {
            _eventHandlers.Clear();
        }
    }
}