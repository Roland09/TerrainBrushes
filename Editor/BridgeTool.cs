using UnityEngine;
using UnityEditor;

namespace UnityEditor
{
    public class BridgeTool : TerrainPaintTool<BridgeTool>
    {
        Material m_Material = null;
        Terrain m_StartTerrain = null;
        private Vector3 m_StartPoint;

        [SerializeField]
        AnimationCurve widthProfile = AnimationCurve.Linear(0, 1, 1, 1);

        [SerializeField]
        AnimationCurve heightProfile = AnimationCurve.Linear(0, 0, 1, 1);

        [SerializeField]
        AnimationCurve strengthProfile = AnimationCurve.Linear(0, 1, 1, 1);

        [SerializeField]
        float m_Spacing = 0.01f;
        

        Material GetPaintMaterial()
        {
            if (m_Material == null)
                m_Material = new Material(Shader.Find("Hidden/TerrainEngine/PaintHeight"));
            return m_Material;
        }

        public override string GetName()
        {
            return "Bridge";
        }

        public override string GetDesc()
        {
            return "Shift + Click to Set the start point, click to connect the bridge.\nCurrently does not work between tiles.";
        }

        public override void OnSceneGUI(SceneView sceneView, Terrain terrain, Texture brushTexture, float brushStrength, int brushSizeInTerrainUnits, float brushRotation = 0.0f, bool holdPosition = false)
        {
            TerrainPaintUtilityEditor.ShowDefaultPreviewBrush(terrain, brushTexture, brushStrength, brushSizeInTerrainUnits, brushRotation, 0.0f, holdPosition);

        }
        public override void OnInspectorGUI(Terrain terrain)
        {
            EditorGUI.BeginChangeCheck();

            widthProfile = EditorGUILayout.CurveField("Width Profile", widthProfile);
            heightProfile = EditorGUILayout.CurveField("Height Profile", heightProfile);
            strengthProfile = EditorGUILayout.CurveField("Strength Profile", strengthProfile);

            m_Spacing = EditorGUILayout.Slider(new GUIContent("Brush Spacing", "Distance between brush splats"), m_Spacing, 1.0f, 100.0f);

            if (EditorGUI.EndChangeCheck())
                Save(true);
        }

        private Vector2 transformToWorld(Terrain t, Vector2 uvs)
        {
            Vector3 tilePos = t.GetPosition();
            //Debug.Log(t.terrainData.size);
            return new Vector2(tilePos.x, tilePos.z) + uvs * new Vector2(t.terrainData.size.x, t.terrainData.size.z);
        }

        private Vector2 transformToUVSpace(Terrain originTile, Vector2 worldPos) {
            Vector3 originTilePos = originTile.GetPosition();
            Vector2 uvPos = new Vector2((worldPos.x - originTilePos.x) / originTile.terrainData.size.x,
                                        (worldPos.y - originTilePos.z) / originTile.terrainData.size.z);
            return uvPos;
        }

        public override bool Paint(Terrain terrain, Texture brushTexture, Vector2 uv, float brushStrength, int brushSize, float brushRotation = 0.0f)
        {
            //grab the starting position & height
            if (Event.current.shift)
            {
                //m_Height = terrain.terrainData.GetInterpolatedHeight(uv.x, uv.y) / terrain.terrainData.size.y;
                float height = terrain.terrainData.GetInterpolatedHeight(uv.x, uv.y) / terrain.terrainData.size.y;
                m_StartPoint = new Vector3(uv.x, uv.y, height);
                m_StartTerrain = terrain;
                return true;
            }

            if (!m_StartTerrain || (Event.current.type == EventType.MouseDrag)) {
                return true;
            }

            //get the target position & height
            float targetHeight = terrain.terrainData.GetInterpolatedHeight(uv.x, uv.y) / terrain.terrainData.size.y;
            Vector3 targetPos = new Vector3(uv.x, uv.y, targetHeight);

            if (terrain != m_StartTerrain) {
                //figure out the stroke vector in uv,height space
                Vector2 targetWorld = transformToWorld(terrain, uv);
                Vector2 targetUVs = transformToUVSpace(m_StartTerrain, targetWorld);
                targetPos.x = targetUVs.x;
                targetPos.y = targetUVs.y;
            }

            Vector3 stroke = targetPos - m_StartPoint;
            float strokeLength = stroke.magnitude;
            int numSplats = (int)(strokeLength / (0.001f * m_Spacing));

            Terrain currTerrain = m_StartTerrain;
            Material mat = GetPaintMaterial();

            Vector2 posOffset = new Vector2(0.0f, 0.0f);
            for(int i = 0; i < numSplats; i++)
            {
                float pct = (float)i / (float)numSplats;

                float widthScale = widthProfile.Evaluate(pct);
                float heightScale = heightProfile.Evaluate(pct);
                float strengthScale = strengthProfile.Evaluate(pct);

                Vector3 currPos = m_StartPoint + pct * stroke;
                currPos.x += posOffset.x;
                currPos.y += posOffset.y;

                if (currPos.x >= 1.0f && (currTerrain.rightNeighbor != null)) {
                    currTerrain = currTerrain.rightNeighbor;
                    currPos.x -= 1.0f;
                    posOffset.x -= 1.0f;
                }
                if(currPos.x <= 0.0f && (currTerrain.leftNeighbor != null)) {
                    currTerrain = currTerrain.leftNeighbor;
                    currPos.x += 1.0f;
                    posOffset.x += 1.0f;
                }
                if(currPos.y >= 1.0f && (currTerrain.topNeighbor != null)) {
                    currTerrain = currTerrain.topNeighbor;
                    currPos.y -= 1.0f;
                    posOffset.y -= 1.0f;
                }
                if(currPos.y <= 0.0f && (currTerrain.bottomNeighbor != null)) {
                    currTerrain = currTerrain.bottomNeighbor;
                    currPos.y += 1.0f;
                    posOffset.y += 1.0f;
                }

                Vector2 currUV = new Vector2(currPos.x, currPos.y);

                int finalBrushSize = (int)(widthScale * (float)brushSize);
                float finalHeight = (m_StartPoint + heightScale * stroke).z;

                Rect brushRect = TerrainPaintUtility.CalculateBrushRect(currTerrain, currUV, finalBrushSize, brushRotation);
                TerrainPaintUtility.PaintContext paintContext = TerrainPaintUtility.BeginPaintHeightmap(currTerrain, brushRect, "Terrain Paint - Bridge");

                Vector4 brushParams = new Vector4(strengthScale * brushStrength * 0.01f, 0.5f * finalHeight, 0.0f, brushRotation);
                mat.SetTexture("_BrushTex", brushTexture);
                mat.SetVector("_BrushParams", brushParams);

                Graphics.Blit(paintContext.sourceRenderTexture, paintContext.destinationRenderTexture, mat, 2);

                TerrainPaintUtility.EndPaintHeightmap(paintContext);
            }
            return false;
        }
    }
}
