using UnityEditor;
using UnityEngine;

namespace Tools
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WaterMeshGenerator : MonoBehaviour
    {
        [Header("Settings")]
        public int GridSize = 100;
        public float CellSize = 1f; 

        [ContextMenu("Generate Water Mesh")]
        public void Generate()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            Mesh mesh = new Mesh();
            mesh.name = "ProceduralWater";

            // 1. Вершины
            Vector3[] vertices = new Vector3[(GridSize + 1) * (GridSize + 1)];
            Vector2[] uvs = new Vector2[vertices.Length];
            
            float offset = GridSize * CellSize * 0.5f;

            for (int i = 0, z = 0; z <= GridSize; z++)
            {
                for (int x = 0; x <= GridSize; x++, i++)
                {
                    vertices[i] = new Vector3(x * CellSize - offset, 0, z * CellSize - offset);
                    uvs[i] = new Vector2((float)x / GridSize, (float)z / GridSize);
                }
            }

            // 2. Треугольники
            int[] triangles = new int[GridSize * GridSize * 6];
            for (int ti = 0, vi = 0, z = 0; z < GridSize; z++, vi++)
            {
                for (int x = 0; x < GridSize; x++, ti += 6, vi++)
                {
                    triangles[ti] = vi;
                    triangles[ti + 3] = triangles[ti + 2] = vi + 1;
                    triangles[ti + 4] = triangles[ti + 1] = vi + GridSize + 1;
                    triangles[ti + 5] = vi + GridSize + 2;
                }
            }

            // 3. Применение
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mf.mesh = mesh;
            Debug.Log($"Water Mesh Generated: {vertices.Length} vertices.");
        }
        
#if UNITY_EDITOR
        [ContextMenu("Save Mesh as Asset")]
        public void SaveAsset()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogError("No mesh to save!");
                return;
            }

            string path = "Assets/Assets/Meshes/WaterSurfaceMesh.asset";
            
            System.IO.Directory.CreateDirectory("Assets/Assets/Meshes");

            AssetDatabase.CreateAsset(mf.sharedMesh, path);
            AssetDatabase.SaveAssets();
        
            Debug.Log($"Mesh saved to {path}");
        }
#endif
    }
}