
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class ErrorAggregator
{
    public interface IErrorContext
    {
        public string AsErrorMessage();
        public void Log();

    }
    
    public struct MissingPrefab : IErrorContext
    {
        /// <summary>
        /// game object which the message applies.
        /// </summary>
        public GameObject? gameobject;

        /// <summary>
        /// the referenced prefab which could not be found
        /// </summary>
        public string prefab;

        public string AsErrorMessage()
        {
            // file,title,message
            return $"{prefab}\tMissing prefab {gameobject.name}\t";
        }

        public void Log()
        {
            Debug.LogError($"{prefab} has missing prefab {gameobject.name}", gameobject);
        }
    }
    

    public struct MissingGameObjectComponent: IErrorContext
    {
        /// <summary>
        /// game object which the message applies.
        /// </summary>
        public GameObject? gameobject;

        public string parentAssetPath;

        public string AsErrorMessage()
        {
            var path = AssetDatabase.GetAssetPath(gameobject);
            var rootpath = AssetDatabase.GetAssetPath(gameobject!.transform.root.gameObject);
            // file,title,message
            return $"{path}\tMissing Component in GameObject {FullPath(gameobject)} in {rootpath}";
        }

        public void Log()
        {
            Debug.LogError($"Missing Component in GameObject: {FullPath(gameobject)} in {parentAssetPath}", gameobject);
        }
    }
    
    public struct MissingGameObjectReference : IErrorContext
    {
        /// <summary>
        /// game object which the message applies.
        /// </summary>
        public GameObject? gameobject;

        /// <summary>
        /// Component with the missing reference
        /// </summary>
        public string componentName;
        /// <summary>
        /// property which has the missing reference
        /// </summary>
        public string propertName;
        
        public string parentAssetPath;
        public string AsErrorMessage()
        {     
            var path = AssetDatabase.GetAssetPath(gameobject);
            var rootpath = AssetDatabase.GetAssetPath(gameobject!.transform.root.gameObject);
            return $"{path}\tMissing Reference in GameObject {FullPath(gameobject)} Component: {componentName}, Property: {propertName} in {rootpath}";
            
        }

        public void Log()
        {
            Debug.LogError($"Missing REFERENCE: [{parentAssetPath}]{FullPath(gameobject)}. Component: {componentName}, Property: {propertName}");
        }
    }
    
    /// <summary>
    /// the full path to the given GameObject, relative to the root
    /// </summary>
    /// <param name="go"></param>
    /// <returns></returns>
    private static string FullPath(GameObject go) {
        if (go.transform != go.transform.root)
        {
            return
                $"{go.transform.root.name}/{AnimationUtility.CalculateTransformPath(go.transform, go.transform.root)}";
        }
        else
        {
            return go.transform.name;
        }
    }

    protected readonly List<IErrorContext> _errors = new();
    protected readonly List<string> _msgs = new();

    public void Capture(IErrorContext e)
    {
        _errors.Add(e);
        // cache the error message immediately, as we dont want any GameObject referenced to disappear
        _msgs.Add(e.AsErrorMessage());
        if (!Application.isBatchMode)
        {  
            e.Log();
        }
    }

    public void Join(ErrorAggregator b)
    {
        _errors.AddRange(b._errors);
        _msgs.AddRange(b._msgs);
    }

    public List<string> ErrorMessages()
    {
        return _msgs;
    }
}
    
    
