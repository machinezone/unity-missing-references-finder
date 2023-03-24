
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class ErrorAggregator : IEnumerable<ErrorAggregator.ErrorContext>
{
    public struct ErrorContext
    {
        public string message;
        public string? prefabName;
        /// <summary>
        /// context – Object to which the message applies.
        /// </summary>
        public object? context;
    }

    private readonly List<ErrorContext> _errors = new();

    public void Capture(ErrorContext e)
    {
        _errors.Add(e);
        Debug.LogError(e.message, e.context as Object);
    }
    public void Capture(string message, string? prefabName = null, object? context = null)
    {
        var e = new ErrorContext() {
            message = message,
            prefabName = prefabName,
            context = context
        };
        _errors.Add(e);
        Debug.LogError(e.message, context as Object);
    }

    public void Join(ErrorAggregator b)
    {
        _errors.AddRange(b);
    }

    public IEnumerator<ErrorContext> GetEnumerator()
    {
        return _errors.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
    
    
