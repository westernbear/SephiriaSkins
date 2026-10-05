using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SephiriaSkins/Export recipe")]
public sealed class SkinExportRecipe : ScriptableObject
{
    [Serializable]
    public sealed class Resource
    {
        public string id;
        public UnityEngine.Object asset;
    }
    public TextAsset manifest;
    public string catalogFile = "../catalog/catalog-1.0.33.json";
    public string outputDirectory = "../examples/unity-pack";
    public List<Resource> resources = new List<Resource>();
}
