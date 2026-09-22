// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Geumeum.Art.EditorTools
{
    /// <summary> 기존 아레나의 판정 구조를 보존하며 사당과 붉은 달 저승의 표현을 구성한다 </summary>
    public static class BossArenaArtBuilder
    {
        private const string SCENE_PATH = "Assets/00.Scenes/Release/Boss_Arena.unity";
        private const string ASSET_ROOT = "Assets/98.FreeAssets/BossArena";
        private const string GENERATED_ROOT = ASSET_ROOT + "/Generated";
        private const string ART_ROOT = "ArenaArt_20260922";

        [MenuItem("그믐/아트/보스 아레나/사당과 붉은 달 구성")]
        public static void Build()
        {
            Scene scene = SceneManager.GetSceneByPath(SCENE_PATH);
            if (!scene.isLoaded || Application.isPlaying)
            {
                throw new InvalidOperationException("Boss_Arena를 편집 모드로 열어야 한다");
            }

            Directory.CreateDirectory(GENERATED_ROOT);
            AssetDatabase.Refresh();
            PrepareModels();
            GameObject[] roots = scene.GetRootGameObjects();
            Transform phase1 = roots.First(x => x.name == "Arena_Phase1").transform;
            Transform phase2 = roots.First(x => x.name == "Arena_Phase2").transform;
            Transform shared = roots.First(x => x.name == "Shared").transform;
            Transform entrance = roots.First(x => x.name == "Antechamber").transform;
            Transform art1 = ReplaceArtRoot(phase1);
            Transform art2 = ReplaceArtRoot(phase2);
            Transform artShared = ReplaceArtRoot(shared);

            Material stone = LitMaterial("AgedGranite", new Color(.23f,.25f,.25f), .24f);
            Material wall = LitMaterial("PlasterAsh", new Color(.28f,.27f,.25f), .12f);
            Material roof = LitMaterial("CharcoalTile", new Color(.085f,.10f,.12f), .32f);
            Material timber = LitMaterial("OldRedTimber", new Color(.17f,.055f,.035f), .16f);
            Material wet = LitMaterial("UnderworldStone", new Color(.085f,.07f,.09f), .82f);
            Material bronze = LitMaterial("WornBronze", new Color(.25f,.19f,.10f), .3f);
            Material floor1 = LitMaterial("WetSlateP1", new Color(.32f,.35f,.38f), .35f);
            Material floor2 = LitMaterial("WetSlateP2", new Color(.16f,.135f,.16f), .9f);
            Texture2D slate = BuildSlateTexture();
            floor1.SetTexture("_BaseMap", slate);
            floor2.SetTexture("_BaseMap", slate);

            BuildFloor(artShared, floor1);
            Renderer floor = artShared.Find("StonePaving").GetComponent<Renderer>();
            shared.Find("Geometry/Slab_Visual_R18").GetComponent<Renderer>().enabled = false;
            var view = shared.GetComponentsInChildren<BossArenaPhaseView>(true).First();
            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("_floorRenderer").objectReferenceValue = floor;
            serializedView.FindProperty("_floorMaterialShrine").objectReferenceValue = floor1;
            serializedView.FindProperty("_floorMaterialUnderworld").objectReferenceValue = floor2;
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            BuildWalls(phase1.Find("Wall_R22"), art1, wall, roof, stone);
            BuildShrine(phase1.Find("Shrine_North_R20"), art1, false, stone, timber, roof);
            BuildShrine(phase2.Find("Shrine_North_R20_Wet"), art2, true, wet, wet, roof);
            BuildCairns(phase1.Find("Rocks"), art1, false, stone);
            BuildCairns(phase2.Find("Rocks_Collapsed"), art2, true, wet);
            ReplaceLanterns(phase1.Find("Lanterns_12"));
            ReplaceLanterns(entrance.Find("Lanterns_2"));
            BuildEntrance(art1, entrance, timber, roof, bronze);
            BuildMountainSilhouette(art1);
            BuildUnderworld(art2, wet);
            ConfigureUnderworldProfiles(phase2);
            CombineBoxDetails(art1);
            CombineBoxDetails(art2);

            phase1.gameObject.SetActive(true);
            phase2.gameObject.SetActive(false);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[BossArenaArt] 사당·붉은 달 아레나 구성 완료. 공유 충돌·NavMesh·스폰 유지");
        }

        private static Transform ReplaceArtRoot(Transform parent)
        {
            Transform old = parent.Find(ART_ROOT);
            if (old != null) { UnityEngine.Object.DestroyImmediate(old.gameObject); }
            var root = new GameObject(ART_ROOT);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        private static void PrepareModels()
        {
            foreach(string name in new[] {"shrine","cairn","lantern"})
            {
                string folder=ASSET_ROOT+"/Meshy/"+name;
                string modelPath=folder+"/model.fbx";
                var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
                if(importer==null) { throw new InvalidOperationException("Meshy FBX가 없다: "+modelPath); }
                importer.addCollider=false;
                importer.importAnimation=false;
                importer.isReadable=false;
                string normalPath=folder+"/normal.png";
                var normalImporter=(TextureImporter)AssetImporter.GetAtPath(normalPath);
                normalImporter.textureType=TextureImporterType.NormalMap;
                normalImporter.maxTextureSize=2048;
                normalImporter.SaveAndReimport();
                string roughPath=folder+"/roughness.png";
                var roughImporter=(TextureImporter)AssetImporter.GetAtPath(roughPath);
                roughImporter.sRGBTexture=false; roughImporter.isReadable=true;
                roughImporter.maxTextureSize=2048; roughImporter.SaveAndReimport();
                Texture2D rough=AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath);
                Color32[] pixels=rough.GetPixels32();
                for(int i=0;i<pixels.Length;++i) { pixels[i]=new Color32(0,0,0,(byte)(255-pixels[i].r)); }
                var packed=new Texture2D(rough.width,rough.height,TextureFormat.RGBA32,false,true);
                packed.SetPixels32(pixels); packed.Apply();
                string packedPath=GENERATED_ROOT+"/"+name+"_MetalSmooth.png";
                File.WriteAllBytes(packedPath,packed.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(packed);
                roughImporter.isReadable=false; roughImporter.SaveAndReimport();
                AssetDatabase.ImportAsset(packedPath);
                var packedImporter=(TextureImporter)AssetImporter.GetAtPath(packedPath);
                packedImporter.sRGBTexture=false; packedImporter.alphaSource=TextureImporterAlphaSource.FromInput;
                packedImporter.SaveAndReimport();
                Material material=LitMaterial(name+"_PBR",Color.white,name=="shrine"?.18f:.75f);
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/base_color.png"));
                material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath));
                material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
                if(name=="shrine")
                {
                    // 낡은 목재와 기와에 날카로운 하이라이트나 거울 반사가 생기지 않게 한다.
                    material.SetFloat("_SpecularHighlights",0);
                    material.SetFloat("_EnvironmentReflections",0);
                    material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                }
                foreach(Material embedded in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>())
                {
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(embedded),material);
                }

                importer.SaveAndReimport();
            }
        }

        private static Texture2D BuildSlateTexture()
        {
            const int size=512;
            var texture=new Texture2D(size,size,TextureFormat.RGB24,false);
            var pixels=new Color[size*size];
            for(int y=0;y<size;++y)
            {
                for (int x=0;x<size;++x)
            {
                float u=x/(float)(size-1),v=y/(float)(size-1);
                float grain=Mathf.PerlinNoise(u*31,v*31)*.1f+Mathf.PerlinNoise(u*7,v*9)*.15f;
                float edge=Mathf.SmoothStep(0,1,Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v))*38);
                float light=(.58f+grain)*Mathf.Lerp(.44f,1,edge);
                pixels[y*size+x]=new Color(light*.95f,light*.98f,light);
            }
            }

            texture.SetPixels(pixels); texture.Apply();
            string path=GENERATED_ROOT+"/SlateSurface.png";
            File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void CombineBoxDetails(Transform root)
        {
            // 반복되는 기와와 담장 장식은 재질별 메시로 합쳐 드로우 콜을 제한한다.
            var boxes=root.Cast<Transform>().Where(t=>t.GetComponent<MeshFilter>()!=null &&
                t.GetComponent<MeshFilter>().sharedMesh.name=="Cube" && t.name!="BloodMoonSky").ToArray();
            foreach(var group in boxes.GroupBy(t=>t.GetComponent<Renderer>().sharedMaterial))
            {
                var combine=group.Select(t=>new CombineInstance {mesh=t.GetComponent<MeshFilter>().sharedMesh,
                    transform=root.worldToLocalMatrix*t.localToWorldMatrix}).ToArray();
                var mesh=new Mesh {name=root.parent.name+"_"+group.Key.name};
                mesh.CombineMeshes(combine,true,true);
                string path=GENERATED_ROOT+"/"+mesh.name+".asset";
                Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(existing==null) { AssetDatabase.CreateAsset(mesh,path); }
                else { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing; }
                var item=new GameObject("Combined_"+group.Key.name,typeof(MeshFilter),typeof(MeshRenderer));
                item.transform.SetParent(root,false); item.layer=LayerMask.NameToLayer("Ignore Raycast");
                item.GetComponent<MeshFilter>().sharedMesh=mesh; item.GetComponent<Renderer>().sharedMaterial=group.Key;
                foreach(Transform box in group) { UnityEngine.Object.DestroyImmediate(box.gameObject); }
            }
        }

        private static Material LitMaterial(string name, Color color, float smoothness)
        {
            string path = GENERATED_ROOT + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position,
            Vector3 size, Material material, Quaternion rotation)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = size;
            item.transform.localRotation = rotation;
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            item.GetComponent<Renderer>().sharedMaterial = material;
            // 장식은 이동·NavMesh·카메라 충돌 대상에 넣지 않는다.
            item.layer = LayerMask.NameToLayer("Ignore Raycast");
            return item;
        }

        private static void BuildFloor(Transform parent, Material material)
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            // 석판 틈은 표현만 한다. 아래 원판과 참배로의 충돌 바닥은 연속으로 유지한다.
            for (int row = -40; row <= 24; ++row)
            {
                float z = row * .94f;
                for (int col = -15; col <= 15; ++col)
                {
                    float x = col * 1.52f + ((row & 1) == 0 ? 0f : .76f);
                    bool yard = new Vector2(x, z).magnitude < 21.2f;
                    bool path = z >= -36.3f && z < -21f && Mathf.Abs(x) < 2.15f;
                    if (!yard && !path) { continue; }
                    float jitter = Mathf.PerlinNoise(col * .7f + 20f, row * .4f + 30f);
                    float halfX = .73f - jitter * .018f;
                    float halfZ = .445f - jitter * .009f;
                    int first = vertices.Count;
                    vertices.Add(new Vector3(x-halfX,.022f,z-halfZ));
                    vertices.Add(new Vector3(x-halfX,.022f,z+halfZ));
                    vertices.Add(new Vector3(x+halfX,.022f,z+halfZ));
                    vertices.Add(new Vector3(x+halfX,.022f,z-halfZ));
                    uv.Add(new Vector2(0,0)); uv.Add(new Vector2(0,1));
                    uv.Add(new Vector2(1,1)); uv.Add(new Vector2(1,0));
                    triangles.AddRange(new[] {first,first+1,first+2,first,first+2,first+3});
                }
            }
            var mesh = new Mesh { name = "BossArena_StonePaving" };
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string meshPath = GENERATED_ROOT + "/StonePaving.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing == null) { AssetDatabase.CreateAsset(mesh,meshPath); }
            else { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing; }
            var item = new GameObject("StonePaving",typeof(MeshFilter),typeof(MeshRenderer));
            item.transform.SetParent(parent,false);
            item.GetComponent<MeshFilter>().sharedMesh=mesh;
            item.GetComponent<Renderer>().sharedMaterial=material;
            item.layer=LayerMask.NameToLayer("Ignore Raycast");
        }

        private static void BuildWalls(Transform old, Transform parent, Material plaster,
            Material tile, Material stone)
        {
            foreach (Transform segment in old)
            {
                Renderer renderer=segment.GetComponent<Renderer>();
                if(renderer==null) { continue; }
                renderer.sharedMaterial=plaster;
                Vector3 p=segment.position;
                Vector3 size=segment.lossyScale;
                Box("WallFoundation",parent,new Vector3(p.x,.3f,p.z),
                    new Vector3(size.x,.6f,size.z+.06f),stone,segment.rotation);
                Box("TileWallCap",parent,new Vector3(p.x,2.17f,p.z),
                    new Vector3(size.x+.04f,.16f,size.z+.24f),tile,segment.rotation);
                int count=Mathf.CeilToInt(size.x/.25f);
                for(int i=0;i<count;++i)
                {
                    Vector3 along=segment.right*((i+.5f)/count-.5f)*size.x;
                    Box("RidgeTile",parent,new Vector3(p.x,2.30f,p.z)+along,
                        new Vector3(.12f,.10f,size.z+.32f),tile,segment.rotation);
                }
            }
        }

        private static GameObject LoadModel(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(ASSET_ROOT+"/Meshy/"+name+"/model.fbx");
        }

        private static GameObject PlaceModel(GameObject source, string name, Transform parent,
            Vector3 basePoint, float width, float yaw, Material overrideMaterial=null)
        {
            var model=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);
            model.name=name;
            model.transform.localPosition=Vector3.zero;
            model.transform.localRotation=Quaternion.identity;
            model.transform.localScale=Vector3.one;
            Renderer[] renderers=model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds=renderers[0].bounds;
            foreach(Renderer r in renderers) { bounds.Encapsulate(r.bounds); }
            float factor=width/Mathf.Max(bounds.size.x,.01f);
            model.transform.localScale=Vector3.one*factor;
            model.transform.rotation=Quaternion.Euler(0,yaw,0);
            bounds=renderers[0].bounds;
            foreach(Renderer r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                r.gameObject.layer=LayerMask.NameToLayer("Ignore Raycast");
                if(overrideMaterial!=null) { r.sharedMaterials=r.sharedMaterials.Select(_=>overrideMaterial).ToArray(); }
            }
            model.transform.position+=basePoint-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            foreach(Collider c in model.GetComponentsInChildren<Collider>(true)) { UnityEngine.Object.DestroyImmediate(c); }
            return model;
        }

        private static void BuildShrine(Transform old, Transform parent, bool underworld,
            Material stone, Material timber, Material roof)
        {
            GameObject source=LoadModel("shrine");
            if(source==null) { throw new InvalidOperationException("Meshy shrine model missing"); }
            foreach(Renderer r in old.GetComponentsInChildren<Renderer>(true)) { r.enabled=false; }
            Material shrine=AssetDatabase.LoadAssetAtPath<Material>(GENERATED_ROOT+"/shrine_PBR.mat");
            if(underworld)
            {
                string path=GENERATED_ROOT+"/ShrineUnderworld.mat";
                Material dark=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(dark==null) { dark=new Material(shrine); AssetDatabase.CreateAsset(dark,path); }
                else { EditorUtility.CopySerialized(shrine,dark); }
                dark.name="ShrineUnderworld";
                dark.SetColor("_BaseColor",new Color(.42f,.36f,.39f));
                dark.SetFloat("_Smoothness",.12f);
                // 반사 제거 후에도 어두운 저승에서 목재·기와의 표면 무늬는 읽히게 한다.
                dark.SetTexture("_EmissionMap",dark.GetTexture("_BaseMap"));
                dark.SetColor("_EmissionColor",new Color(.18f,.13f,.10f));
                dark.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
                dark.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(dark); shrine=dark;
            }
            PlaceModel(source,"Shrine_MainHall",parent,new Vector3(0,0,24),13,180,shrine);
            for(int i=0;i<3;++i)
            {
                Box("ApproachStep",parent,new Vector3(0,.1f+i*.2f,16.5f+i*1.1f),
                    new Vector3(12-i,.2f,1.3f),stone,Quaternion.identity);
            }
        }

        private static void BuildCairns(Transform old, Transform parent, bool collapsed, Material stone)
        {
            GameObject source=LoadModel("cairn");
            if(source==null) { throw new InvalidOperationException("Meshy cairn model missing"); }
            int i=0;
            foreach(Transform landmark in old)
            {
                Vector3 point=new Vector3(landmark.position.x,0,landmark.position.z);
                foreach(Renderer r in landmark.GetComponentsInChildren<Renderer>(true)) { r.enabled=false; }
                GameObject model=PlaceModel(source,landmark.name+"_Art",parent,point,1.5f+i*.12f,35+i*79,collapsed?stone:null);
                Renderer[] renderers=model.GetComponentsInChildren<Renderer>();
                Bounds bounds=renderers[0].bounds;
                foreach(Renderer r in renderers) { bounds.Encapsulate(r.bounds); }
                model.transform.localScale*= (collapsed?.65f:1.2f)/bounds.size.y;
                if(collapsed) { model.transform.Rotate(0,0,53,Space.Self); }
                bounds=renderers[0].bounds;
                foreach(Renderer r in renderers) { bounds.Encapsulate(r.bounds); }
                model.transform.position-=Vector3.up*bounds.min.y;
                ++i;
            }
        }

        private static void ReplaceLanterns(Transform root)
        {
            GameObject source=LoadModel("lantern");
            if(source==null) { throw new InvalidOperationException("Meshy lantern model missing"); }
            foreach(Transform lantern in root)
            {
                Transform existing=lantern.Find("MeshyLantern");
                if(existing!=null) { UnityEngine.Object.DestroyImmediate(existing.gameObject); }
                foreach(Renderer r in lantern.GetComponentsInChildren<Renderer>(true)) { r.enabled=false; }
                PlaceModel(source,"MeshyLantern",lantern,new Vector3(lantern.position.x,0,lantern.position.z),.8f,0);
            }
        }

        private static void BuildEntrance(Transform parent, Transform entrance, Material timber,
            Material roof, Material bronze)
        {
            Box("GateLeftPillar",parent,new Vector3(-3.4f,2.3f,-22.5f),new Vector3(.42f,4.6f,.42f),timber,Quaternion.identity);
            Box("GateRightPillar",parent,new Vector3(3.4f,2.3f,-22.5f),new Vector3(.42f,4.6f,.42f),timber,Quaternion.identity);
            Box("GateLintel",parent,new Vector3(0,4.25f,-22.5f),new Vector3(8,.42f,.55f),timber,Quaternion.identity);
            Box("GateTileCrown",parent,new Vector3(0,4.55f,-22.5f),new Vector3(8.6f,.2f,1.2f),roof,Quaternion.identity);
            foreach(Renderer r in entrance.Find("HatRack_Empty").GetComponentsInChildren<Renderer>()) { r.sharedMaterial=timber; }
            // 참배로는 지붕 없이 남겨 하늘과 붉은 달을 올려다볼 수 있게 한다.
        }

        private static void BuildUnderworld(Transform parent, Material stone)
        {
            var sky=new GameObject("BloodMoonSky",typeof(MeshFilter),typeof(MeshRenderer));
            sky.transform.SetParent(parent,false);
            sky.transform.localScale=Vector3.one*700;
            GameObject cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
            sky.GetComponent<MeshFilter>().sharedMesh=cube.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(cube);
            Material material=AssetDatabase.LoadAssetAtPath<Material>(GENERATED_ROOT+"/BloodMoonSky.mat");
            if(material==null)
            {
                material=new Material(Shader.Find("Geumeum/Environment/BloodMoonSky"));
                AssetDatabase.CreateAsset(material,GENERATED_ROOT+"/BloodMoonSky.mat");
            }
            ConfigureBloodMoonMaterial(material);
            Renderer renderer=sky.GetComponent<Renderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            sky.layer=LayerMask.NameToLayer("Ignore Raycast");
            Material water=LitMaterial("UnderworldWater",new Color(.028f,.016f,.027f),.94f);
            Box("UnderworldWater",parent,new Vector3(0,-.24f,0),new Vector3(580,.1f,580),water,Quaternion.identity);
            BuildUnderworldBackdrop(parent);
            BuildUnderworldRain(parent);
            // 먼 실루엣은 통행 영역 밖에만 둔다. 중앙의 패턴 공간과 네 표식은 건드리지 않는다.
            for(int i=0;i<18;++i)
            {
                float a=(i*137.5f)*Mathf.Deg2Rad;
                float r=29+(i%4)*3.3f;
                Vector3 point=new Vector3(Mathf.Sin(a)*r,1.3f,Mathf.Cos(a)*r);
                if(point.z<-21 && Mathf.Abs(point.x)<7) { continue; }
                Box("DrownedMemorial",parent,point,new Vector3(.4f,2.6f+(i%3)*.45f,.5f),stone,Quaternion.Euler(i%2==0?8:-12,i*57,7));
            }
        }

        private static void BuildUnderworldBackdrop(Transform parent)
        {
            // 외곽에만 절벽과 부유 파편을 만들며 이동·NavMesh에는 참여하지 않는다.
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            const int segments=128;
            for(int ring=0;ring<3;++ring)
            {
                for (int i=0;i<=segments;++i)
            {
                float a=i*Mathf.PI*2/segments;
                float radius=54+ring*27;
                float peak=Mathf.PerlinNoise(Mathf.Sin(a)*5+17,Mathf.Cos(a)*5+23);
                float height=4+peak*peak*30+ring*5;
                vertices.Add(new Vector3(Mathf.Sin(a)*radius,-9,Mathf.Cos(a)*radius));
                vertices.Add(new Vector3(Mathf.Sin(a)*(radius+9),height,Mathf.Cos(a)*(radius+9)));
                if(i==0) { continue; }
                int b=vertices.Count-4;
                triangles.AddRange(new[] {b,b+1,b+2,b+2,b+1,b+3});
            }
            }

            SaveBackdropMesh(parent,"UnderworldRidges",vertices,triangles,new Color(.13f,.025f,.039f));
            vertices.Clear(); triangles.Clear();
            for(int i=0;i<36;++i)
            {
                float a=i*137.5f*Mathf.Deg2Rad;
                float radius=33+(i%6)*4;
                Vector3 center=new Vector3(Mathf.Sin(a)*radius,4+(i%7)*2.1f,Mathf.Cos(a)*radius);
                Vector3 size=new Vector3(1.1f+(i%3)*.8f,.6f+(i%4)*.6f,1.2f+(i%5)*.4f);
                Quaternion rotation=Quaternion.Euler(i*37,i*61,i*19);
                // 단순 팔면체 대신 여러 단면의 반경을 흔들어 돌마다 다른 깨진 윤곽을 만든다.
                const int rings=8, sides=13;
                var points=new Vector3[(rings+1)*sides];
                for(int ring=0;ring<=rings;++ring)
                {
                    for (int side=0;side<sides;++side)
                {
                    float latitude=Mathf.PI*ring/rings;
                    float longitude=Mathf.PI*2*side/sides;
                    var direction=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),
                        Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));
                    float noise=Mathf.PerlinNoise(direction.x*3.2f+i*7.13f+10,
                        direction.z*3.2f+direction.y*1.7f+i*3.71f+20);
                    float rough=.70f+noise*.65f;
                    points[ring*sides+side]=center+rotation*Vector3.Scale(direction*rough,size);
                }
                }

                for (int ring=0;ring<rings;++ring)
                {
                    for (int side=0;side<sides;++side)
                {
                    int next=(side+1)%sides;
                    Vector3 a0=points[ring*sides+side], b0=points[ring*sides+next];
                    Vector3 c0=points[(ring+1)*sides+side], d0=points[(ring+1)*sides+next];
                    if(ring>0) { AppendRockFace(vertices,triangles,center,a0,b0,c0); }
                    if(ring<rings-1) { AppendRockFace(vertices,triangles,center,b0,d0,c0); }
                }
                }
            }
            SaveBackdropMesh(parent,"SuspendedRubble",vertices,triangles,new Color(.074f,.018f,.027f));
        }

        private static void AppendRockFace(List<Vector3> vertices,List<int> triangles,
            Vector3 center,Vector3 a,Vector3 b,Vector3 c)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c)/3-center)<0)
            { Vector3 swap=b; b=c; c=swap; }
            int start=vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(start); triangles.Add(start+1); triangles.Add(start+2);
        }

        private static void SaveBackdropMesh(Transform parent,string name,List<Vector3> vertices,
            List<int> triangles,Color color)
        {
            var mesh=new Mesh {name=name}; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path=GENERATED_ROOT+"/"+name+".asset";
            Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null) { AssetDatabase.CreateAsset(mesh,path); }
            else
            {
                // 직렬화 복사만 하면 에디터의 GPU 메시가 이전 형상을 표시할 수 있다.
                existing.Clear(); existing.SetVertices(vertices); existing.SetTriangles(triangles,0);
                existing.RecalculateNormals(); existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(mesh); mesh=existing;
            }
            Material material=LitMaterial(name,color,.12f);
            material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor",color*.65f);
            EditorUtility.SetDirty(material);
            var item=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            item.transform.SetParent(parent,false); item.layer=LayerMask.NameToLayer("Ignore Raycast");
            item.GetComponent<MeshFilter>().sharedMesh=mesh;
            item.GetComponent<Renderer>().sharedMaterial=material;
        }

        private static void BuildMountainSilhouette(Transform parent)
        {
            Material material=LitMaterial("DistantMountain",new Color(.035f,.052f,.046f),.05f);
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            const int segments=96;
            for(int ring=0;ring<2;++ring)
            {
                for (int i=0;i<=segments;++i)
            {
                float angle=i*Mathf.PI*2/segments;
                float radius=62+ring*30;
                float height=8+Mathf.PerlinNoise(Mathf.Cos(angle)*2+8,Mathf.Sin(angle)*2+8)*24+ring*8;
                vertices.Add(new Vector3(Mathf.Sin(angle)*radius,-4,Mathf.Cos(angle)*radius));
                vertices.Add(new Vector3(Mathf.Sin(angle)*(radius+12),height,Mathf.Cos(angle)*(radius+12)));
                if(i==0) { continue; }
                int b=vertices.Count-4;
                triangles.AddRange(new[] {b,b+1,b+2,b+2,b+1,b+3});
            }
            }

            var mesh=new Mesh {name="MountainSilhouette"}; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path=GENERATED_ROOT+"/MountainSilhouette.asset";
            Mesh old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old==null) { AssetDatabase.CreateAsset(mesh,path); }
            else { EditorUtility.CopySerialized(mesh,old); UnityEngine.Object.DestroyImmediate(mesh); mesh=old; }
            var item=new GameObject("MountainSilhouette",typeof(MeshFilter),typeof(MeshRenderer));
            item.transform.SetParent(parent,false); item.layer=LayerMask.NameToLayer("Ignore Raycast");
            item.GetComponent<MeshFilter>().sharedMesh=mesh; item.GetComponent<Renderer>().sharedMaterial=material;
        }

        /// <summary> 기존 지형과 장식을 다시 만들지 않고 2페이즈 날씨만 갱신한다 </summary>
        [MenuItem("그믐/아트/보스 아레나/2페이즈 비와 붉은 안개 구성")]
        public static void RefreshUnderworldWeather()
        {
            Scene scene=SceneManager.GetSceneByPath(SCENE_PATH);
            if (!scene.isLoaded || Application.isPlaying)
            {
                throw new InvalidOperationException("Boss_Arena를 편집 모드로 열어야 한다");
            }
            Transform phase2=scene.GetRootGameObjects().First(x=>x.name=="Arena_Phase2").transform;
            Transform art=phase2.Find(ART_ROOT);
            Transform previous=art.Find("UnderworldRain");
            if (previous!=null) { UnityEngine.Object.DestroyImmediate(previous.gameObject); }
            BuildUnderworldRain(art);
            ConfigureBloodMoonMaterial(art.Find("BloodMoonSky").GetComponent<Renderer>().sharedMaterial);
            ConfigureUnderworldProfiles(phase2);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureBloodMoonMaterial(Material material)
        {
            material.SetVector("_MoonDirection",new Vector4(.40f,.38f,1,0));
            material.SetFloat("_MoonRadius",.15f);
            material.SetColor("_Zenith",new Color(.085f,.008f,.023f));
            material.SetColor("_Horizon",new Color(.29f,.039f,.052f));
            material.SetColor("_MoonColor",new Color(2.2f,1.4f,1.2f));
            material.SetTexture("_MoonSurface",AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/99.ThirdParty/Background/WeatherMaker/Prefab/Textures/SkySphere/WeatherMakerSkySphereMoonTexture1.png"));
            EditorUtility.SetDirty(material);
        }

        /// <summary> 페이즈 루트와 함께 꺼지는 WeatherMaker 강우 구역. 공용 날씨에는 쓰지 않는다 </summary>
        private static void BuildUnderworldRain(Transform parent)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/99.ThirdParty/Background/WeatherMaker/Prefab/WeatherMakerRainZone.prefab");
            var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
            PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            root.name="UnderworldRain";
            root.transform.localPosition=Vector3.zero;
            var rain=root.GetComponent<DigitalRuby.WeatherMaker.WeatherMakerFallingParticleScript3D>();
            rain.IsFirstPerson=false;
            rain.Intensity=.58f;
            rain.BaseEmissionRate=6000;
            rain.BaseMistEmissionRate=22;
            rain.MistThreshold=.2f;
            rain.MistIntensityMultiplier=.8f;
            rain.PrecipitationTintColor=new Color(.74f,.59f,.63f,.72f);
            rain.PrecipitationMistTintColor=new Color(.85f,.14f,.18f,1);
            rain.PrecipitationSecondaryTintColor=new Color(.65f,.27f,.31f,.35f);
            rain.AnimatedTextureRenderer.gameObject.SetActive(false);

            var drops=rain.ParticleSystem;
            drops.transform.localPosition=new Vector3(0,18,0);
            var shape=drops.shape; shape.radius=37f;
            var main=drops.main; main.maxParticles=4000; main.startLifetime=.85f;
            var velocity=drops.velocityOverLifetime;
            velocity.space=ParticleSystemSimulationSpace.World;
            velocity.x=1.8f; velocity.y=-28f; velocity.z=.65f;

            var mist=rain.MistParticleSystem;
            mist.transform.localPosition=new Vector3(0,.65f,0);
            var mistShape=mist.shape; mistShape.radius=30f;
            var mistMain=mist.main; mistMain.maxParticles=96; mistMain.startLifetime=7f;
            mistMain.startSize=new ParticleSystem.MinMaxCurve(4f,8f);
            // 원본 프리팹의 하향 초속을 남기면 안개가 바닥 아래로 떨어져 보이지 않는다.
            mistMain.startSpeed=0; mistMain.gravityModifier=0;
            mistMain.startColor=new Color(.85f,.14f,.18f,.25f);
            var mistVelocity=mist.velocityOverLifetime;
            mistVelocity.enabled=true; mistVelocity.space=ParticleSystemSimulationSpace.World;
            mistVelocity.x=.45f; mistVelocity.y=.03f; mistVelocity.z=.18f;
            var mistRenderer=mist.GetComponent<ParticleSystemRenderer>();
            string mistPath=GENERATED_ROOT+"/UnderworldRedMist.mat";
            Material mistMaterial=AssetDatabase.LoadAssetAtPath<Material>(mistPath);
            if(mistMaterial==null)
            {
                mistMaterial=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mistMaterial,mistPath);
            }
            // 빛이 없는 외곽에서도 붉은 수증기를 읽을 수 있게 한다. 입자 생성은 WeatherMaker가 담당한다.
            mistMaterial.SetTexture("_BaseMap",mistRenderer.sharedMaterial.mainTexture);
            mistMaterial.SetColor("_BaseColor",new Color(.85f,.18f,.23f,.7f));
            mistMaterial.SetFloat("_Surface",1); mistMaterial.SetFloat("_Blend",0);
            mistMaterial.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            mistMaterial.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            mistMaterial.SetFloat("_ZWrite",0);
            mistMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mistMaterial.renderQueue=3000;
            mistRenderer.sharedMaterial=mistMaterial;
            EditorUtility.SetDirty(mistMaterial);
            foreach(var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system!=drops && system!=mist) { system.gameObject.SetActive(false); }
                var collision=system.collision; collision.enabled=false;
            }
            foreach(var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.receiveShadows=false;
            }
        }

        private static void ConfigureUnderworldProfiles(Transform phase2)
        {
            var light=phase2.Find("Light_Moon_Directional_P2").GetComponent<Light>();
            light.color=new Color(1,.28f,.18f); light.intensity=.38f;
            light.transform.rotation=Quaternion.Euler(22,180,0);
            var fill=phase2.Find("Light_Underworld_Fill").GetComponent<Light>();
            fill.color=new Color(.45f,.39f,.48f);
            foreach(var l in phase2.Find("Foxfire_14").GetComponentsInChildren<Light>(true)) { l.intensity=.65f; }
            var fog=AssetDatabase.LoadAssetAtPath<DigitalRuby.WeatherMaker.WeatherMakerFullScreenFogProfileScript>("Assets/09.Data/Weather/WM_Profile_Underworld.asset");
            fog.FogColor=new Color(.24f,.035f,.055f,1);
            // 하늘과 외곽 능선까지 읽혀야 하므로 먼 거리에서도 완전히 덮지 않는다.
            fog.FogDensity=.032f;
            fog.MaxFogFactor=.64f;
            fog.FogHeight=7f;
            fog.FogNoiseMultiplier=.55f;
            fog.FogNoiseScale=.055f;
            fog.FogNoiseVelocity=new Vector3(.04f,.008f,.02f);
            fog.FogEmissionColor=new Color(.012f,.001f,.002f,1);
            EditorUtility.SetDirty(fog);
            var sky=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/09.Data/Weather/WM_Sky_BossUnderworld.asset");
            var skyProperties=new SerializedObject(sky);
            var gradient=new Gradient();
            gradient.SetKeys(new[] {new GradientColorKey(new Color(.022f,.009f,.016f),0),
                new GradientColorKey(new Color(.10f,.022f,.032f),.55f),
                new GradientColorKey(new Color(.016f,.008f,.022f),1)},
                new[] {new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});
            skyProperties.FindProperty("SkyGradient").gradientValue=gradient;
            skyProperties.FindProperty("SkyGroundGradient").gradientValue=gradient;
            skyProperties.ApplyModifiedPropertiesWithoutUndo();
            var volume=phase2.Find("Volume_P2").GetComponent<Volume>();
            if(volume.sharedProfile.TryGet<ColorAdjustments>(out var color)) { color.saturation.Override(-12); }
            if(volume.sharedProfile.TryGet<WhiteBalance>(out var white)) { white.temperature.Override(-3); }
            EditorUtility.SetDirty(volume.sharedProfile);
        }
    }
}
