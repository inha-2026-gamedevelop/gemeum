using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using Geumeum.Common;
namespace Geumeum.Art.Editor
{
public static class BowLaserVfxBuilder
{
private const string Root = "Assets/02.Prefabs/Art/VFX/BowLaser";
public static void Build()
{
    EnsureFolder(Root); EnsureFolder(Root+"/Shaders");
    BuildGraphs(); BuildPrefabs(); UpgradeEnergyDetail(); UpgradeArrowDetail();
}
private static void EnsureFolder(string path)
{
 if(AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent=System.IO.Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent);
 AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(path));
}
private static void BuildGraphs()
{
// 붉은 흑심 빔 VFX용 Shader Graph 3종을 리플렉션으로 조립한다. 모두 URP Unlit + Support VFX Graph.
var log = new System.Text.StringBuilder();
var problems = new System.Collections.Generic.List<string>();
const string DIR = "Assets/02.Prefabs/Art/VFX/BowLaser/Shaders";
const System.Reflection.BindingFlags ANY = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;

System.Reflection.Assembly SG = null, URP = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (a.GetName().Name == "Unity.ShaderGraph.Editor")
                {
                    SG = a;
                }

                if (a.GetName().Name == "Unity.RenderPipelines.Universal.Editor")
                {
                    URP = a;
                }
            }
System.Func<string, System.Type> TSG = n => SG.GetType(n);
System.Func<System.Type, string, System.Reflection.PropertyInfo> PROP = (t0, n0) => {
    for (var t1 = t0; t1 != null; t1 = t1.BaseType)
    { var p = t1.GetProperty(n0, ANY | System.Reflection.BindingFlags.DeclaredOnly); if (p != null)
        {
            return p;
        }
    }
    return null;
};
System.Func<System.Type, string, System.Reflection.FieldInfo> FIELD = (t0, n0) => {
    for (var t1 = t0; t1 != null; t1 = t1.BaseType)
    { var f = t1.GetField(n0, ANY | System.Reflection.BindingFlags.DeclaredOnly); if (f != null)
        {
            return f;
        }
    }
    return null;
};

if (!UnityEditor.AssetDatabase.IsValidFolder(DIR))
{
    UnityEditor.AssetDatabase.CreateFolder("Assets/02.Prefabs/Art/VFX/BowLaser", "Shaders");
}

var tGraph = TSG("UnityEditor.ShaderGraph.GraphData");
var tTarget = URP.GetType("UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget");
var tUnlit = URP.GetType("UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget");
var tBlockFields = TSG("UnityEditor.ShaderGraph.BlockFields");
var tBFD = TSG("UnityEditor.ShaderGraph.BlockFieldDescriptor");
var tTargetBase = TSG("UnityEditor.ShaderGraph.Target");
var tAbsNode = TSG("UnityEditor.ShaderGraph.AbstractMaterialNode");
var tSlot = TSG("UnityEditor.ShaderGraph.MaterialSlot");
var tFileUtil = TSG("UnityEditor.ShaderGraph.FileUtilities");
var tCategory = TSG("UnityEditor.ShaderGraph.CategoryData");

System.Func<string, object> BLOCKDESC = path => {
    var parts = path.Split('.');
    var nested = tBlockFields.GetNestedType(parts[0], ANY);
    return nested.GetField(parts[1], ANY).GetValue(null);
};

object graph = null;
string curName = null;

System.Action<string, string, string> NEWGRAPH = (name, alphaMode, renderFace) => {
    curName = name;
    var target = System.Activator.CreateInstance(tTarget, true);
    tTarget.GetMethod("TrySetActiveSubTarget", ANY).Invoke(target, new object[] { tUnlit });
    var surfT = URP.GetType("UnityEditor.Rendering.Universal.ShaderGraph.SurfaceType");
    FIELD(tTarget, "m_SurfaceType").SetValue(target, System.Enum.Parse(surfT, "Transparent"));
    var amT = URP.GetType("UnityEditor.Rendering.Universal.ShaderGraph.AlphaMode");
    FIELD(tTarget, "m_AlphaMode").SetValue(target, System.Enum.Parse(amT, alphaMode));
    var rfT = URP.GetType("UnityEditor.Rendering.Universal.ShaderGraph.RenderFace");
    FIELD(tTarget, "m_RenderFace").SetValue(target, System.Enum.Parse(rfT, renderFace));
    var zwT = URP.GetType("UnityEditor.Rendering.Universal.ShaderGraph.ZWriteControl");
    FIELD(tTarget, "m_ZWriteControl").SetValue(target, System.Enum.Parse(zwT, "ForceDisabled"));
    FIELD(tTarget, "m_SupportVFX").SetValue(target, true);
    var targets = System.Array.CreateInstance(tTargetBase, 1);
    targets.SetValue(target, 0);
    string[] bn = { "VertexDescription.Position", "VertexDescription.Normal", "VertexDescription.Tangent",
        "SurfaceDescription.BaseColor", "SurfaceDescription.Alpha" };
    var blocks = System.Array.CreateInstance(tBFD, bn.Length);
    for (int i = 0; i < bn.Length; i++)
    {
        blocks.SetValue(BLOCKDESC(bn[i]), i);
    }

    graph = System.Activator.CreateInstance(tGraph, true);
    tGraph.GetMethod("AddContexts", ANY).Invoke(graph, null);
    tGraph.GetMethod("InitializeOutputs", ANY).Invoke(graph, new object[] { targets, blocks });
    var mDef = tCategory.GetMethod("DefaultCategory", ANY);
    var defArgs = new object[mDef.GetParameters().Length];
    tGraph.GetMethod("AddCategory", ANY).Invoke(graph, new object[] { mDef.Invoke(null, defArgs) });
    PROP(tGraph, "path").SetValue(graph, "Geumeum/VFX");
};

var miAddNode = tGraph.GetMethod("AddNode", ANY);
System.Func<string, float, float, object> NODE = (tn, x, y) => {
    var t = TSG("UnityEditor.ShaderGraph." + tn);
    if (t == null) { problems.Add(curName + " no node type " + tn); return null; }
    var n = System.Activator.CreateInstance(t, true);
    miAddNode.Invoke(graph, new object[] { n, false });
    var ds = PROP(tAbsNode, "drawState").GetValue(n);
    var pos = ds.GetType().GetProperty("position");
    pos.SetValue(ds, new UnityEngine.Rect(x, y, 200, 120));
    PROP(tAbsNode, "drawState").SetValue(n, ds);
    return n;
};


// 슬롯 목록: AbstractMaterialNode.GetSlots<MaterialSlot>(List<MaterialSlot>)
System.Func<object, System.Collections.IList> ALLSLOTS = n => {
    var listT = typeof(System.Collections.Generic.List<>).MakeGenericType(tSlot);
    var list = (System.Collections.IList)System.Activator.CreateInstance(listT);
    foreach (var mm in tAbsNode.GetMethods(ANY))
    {
        if (mm.Name == "GetSlots" && mm.IsGenericMethod && mm.GetParameters().Length == 1)
        {
            mm.MakeGenericMethod(tSlot).Invoke(n, new object[] { list });
            break;
        }
    }
    return list;
};
System.Func<object, string, bool, object> SLOTBY = (n, sname, isInput) => {
    if (sname == null)
    {
        foreach (var s0 in ALLSLOTS(n))
        {
            if ((bool)PROP(tSlot, "isInputSlot").GetValue(s0) == isInput)
            {
                return s0;
            }
        }
    }
    foreach (var s in ALLSLOTS(n))
    {
        var dn = (string)PROP(tSlot, "displayName").GetValue(s);
        int paren = dn.LastIndexOf('(');
        if (paren > 0 && dn.EndsWith(")"))
        {
            dn = dn.Substring(0, paren);
        }

        var sn = (string)PROP(tSlot, "shaderOutputName").GetValue(s);
        var inp = (bool)PROP(tSlot, "isInputSlot").GetValue(s);
        if (inp == isInput && (dn == sname || sn == sname))
        {
            return s;
        }
    }
    problems.Add(curName + " slot missing " + n.GetType().Name + "." + sname + (isInput ? "(in)" : "(out)"));
    return null;
};
System.Func<object, string> DUMPSLOTS = n => {
    var sb = new System.Text.StringBuilder(n.GetType().Name + ":");
    foreach (var s in ALLSLOTS(n))
    {
        sb.Append(" [").Append((bool)PROP(tSlot, "isInputSlot").GetValue(s) ? "in " : "out ")
          .Append(PROP(tSlot, "id").GetValue(s)).Append(" ").Append(PROP(tSlot, "displayName").GetValue(s)).Append("]");
    }

    return sb.ToString();
};
var miConnect = tGraph.GetMethod("Connect", ANY);
var miSlotRef = tAbsNode.GetMethod("GetSlotReference", ANY);
System.Action<object, string, object, string> LINK = (a, aOut, b, bIn) => {
    var sa = SLOTBY(a, aOut, false);
    var sb2 = SLOTBY(b, bIn, true);
    if (sa == null || sb2 == null)
    {
        return;
    }

    var ra = miSlotRef.Invoke(a, new object[] { PROP(tSlot, "id").GetValue(sa) });
    var rb = miSlotRef.Invoke(b, new object[] { PROP(tSlot, "id").GetValue(sb2) });
    try { miConnect.Invoke(graph, new object[] { ra, rb }); }
    catch (System.Exception ex) { problems.Add(curName + " connect " + a.GetType().Name + "." + aOut + "->" + b.GetType().Name + "." + bIn + " " + ex.GetBaseException().Message); }
};
// 입력 슬롯 기본값
System.Action<object, string, object> SETIN = (n, sname, v) => {
    var s = SLOTBY(n, sname, true);
    if (s == null)
    {
        return;
    }

    var vp = s.GetType().GetProperty("value", ANY);
    if (vp == null) { problems.Add(curName + " no value on " + sname); return; }
    object vv = v;
    var vt = vp.PropertyType;
    if (vt == typeof(UnityEngine.Matrix4x4))
    {
        var m = new UnityEngine.Matrix4x4();
        if (v is float) { float f = (float)v; for (int i = 0; i < 16; i++)
            {
                m[i] = f;
            }
        }
        else if (v is UnityEngine.Vector4) { var q = (UnityEngine.Vector4)v; for (int c = 0; c < 4; c++)
            {
                m.SetRow(c, q);
            }
        }
        vv = m;
    }
    else if (vt == typeof(UnityEngine.Vector4) && v is float)
    {
        vv = new UnityEngine.Vector4((float)v, (float)v, (float)v, (float)v);
    }
    else if (vt == typeof(UnityEngine.Vector4) && v is UnityEngine.Vector2) { var q = (UnityEngine.Vector2)v; vv = new UnityEngine.Vector4(q.x, q.y, 0, 0); }
    else if (vt == typeof(UnityEngine.Vector2) && v is float)
    {
        vv = new UnityEngine.Vector2((float)v, (float)v);
    }

    try { vp.SetValue(s, vv); }
    catch (System.Exception ex) { problems.Add(curName + " setin " + sname + " " + ex.GetBaseException().Message); }
};
System.Action<object, string, object> SETTING = (n, fname, v) => {
    var f = FIELD(n.GetType(), fname);
    if (f == null) { problems.Add(curName + " no field " + n.GetType().Name + "." + fname); return; }
    object vv = v is string && f.FieldType.IsEnum ? System.Enum.Parse(f.FieldType, (string)v) : v;
    f.SetValue(n, vv);
};

// 속성: 이름 = 참조명. VFX 출력에서 같은 이름의 입력 슬롯이 된다.
var miAddInput = tGraph.GetMethod("AddGraphInput", ANY);
System.Func<string, string, object, float, float, object> PROPNODE = (kind, refName, val, x, y) => {
    object p = null;
    if (kind == "Color")
    {
        p = System.Activator.CreateInstance(TSG("UnityEditor.ShaderGraph.Internal.ColorShaderProperty"), true);
        PROP(p.GetType(), "value").SetValue(p, (UnityEngine.Color)val);
        var cm = FIELD(p.GetType(), "m_ColorMode") ?? null;
        var cmp = PROP(p.GetType(), "colorMode");
        cmp.SetValue(p, System.Enum.Parse(cmp.PropertyType, "HDR"));
    }
    else if (kind == "Float")
    {
        p = System.Activator.CreateInstance(TSG("UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty"), true);
        PROP(p.GetType(), "value").SetValue(p, (float)val);
    }
    else if (kind == "Tex")
    {
        p = System.Activator.CreateInstance(TSG("UnityEditor.ShaderGraph.Internal.Texture2DShaderProperty"), true);
        var st = PROP(p.GetType(), "value").GetValue(p);
        if (st == null) { st = System.Activator.CreateInstance(PROP(p.GetType(), "value").PropertyType); }
        PROP(st.GetType(), "texture").SetValue(st, (UnityEngine.Texture)val);
        PROP(p.GetType(), "value").SetValue(p, st);
    }
    PROP(p.GetType(), "displayName").SetValue(p, refName);
    var orn = PROP(p.GetType(), "overrideReferenceName");
    if (orn != null)
    {
        orn.SetValue(p, refName);
    }

    miAddInput.Invoke(graph, new object[] { p, -1 });
    var pn = NODE("PropertyNode", x, y);
    PROP(pn.GetType(), "property").SetValue(pn, p);
    return pn;
};
System.Func<string, object> BLOCK = name => {
    var getNodes = tGraph.GetMethod("GetNodes", ANY).MakeGenericMethod(TSG("UnityEditor.ShaderGraph.BlockNode"));
    foreach (var bnode in (System.Collections.IEnumerable)getNodes.Invoke(graph, null))
    {
        var desc = PROP(bnode.GetType(), "descriptor").GetValue(bnode);
        if ((string)PROP(desc.GetType(), "name").GetValue(desc) == name)
        {
            return bnode;
        }
    }
    problems.Add(curName + " no block " + name);
    return null;
};
System.Action SAVE = () => {
    string path = DIR + "/" + curName + ".shadergraph";
    tFileUtil.GetMethod("WriteShaderGraphToDisk", ANY).Invoke(null, new object[] { path, graph });
    UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
    log.AppendLine("saved " + path);
};
// 편의: 이항 연산 노드
System.Func<string, object, string, object, string, float, float, object> OP2 = (tn, a, aOut, b, bOut, x, y) => {
    var n = NODE(tn, x, y);
    if (a != null)
    {
        LINK(a, aOut, n, "A");
    }

    if (b != null)
    {
        LINK(b, bOut, n, "B");
    }

    return n;
};


foreach (var isRing in new [] {false, true})
{
    NEWGRAPH(isRing ? "SG_BowLaserRing" : "SG_BowLaserBeam", "Additive", "Both");
    var age = PROPNODE("Float", "_Age", 0f, -1700, -500);
    var opacity = PROPNODE("Float", "_Opacity", 1f, -1700, -350);
    var dissolve = PROPNODE("Float", "_Dissolve", 0f, -1700, -250);
    var red = PROPNODE("Color", "_EdgeColor", new Color(2.8f, 0.025f, 0.12f, 1f), -1700, 300);
    var white = PROPNODE("Color", "_CoreColor", new Color(3.8f, 0.85f, 1.2f, 1f), -1700, 450);
    var uv = NODE("UVNode", -1700, 0);
    var flow = OP2("MultiplyNode", age, null, null, null, -1500,-500); SETIN(flow,"B",-2.3f);
    var combine = NODE("CombineNode",-1320,-500); LINK(flow,"Out",combine,"G");
    var pan = NODE("TilingAndOffsetNode", -1140,-400); LINK(uv,null,pan,"UV"); LINK(combine,"RG",pan,"Offset"); SETIN(pan,"Tiling",new Vector2(3f,8f));
    var texture=PROPNODE("Tex","_NoiseTexture",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/03.Image/Player/CommonNoise.png"),-1700,-750);
    var sample=NODE("SampleTexture2DNode",-940,-400); LINK(texture,null,sample,"Texture"); LINK(pan,"Out",sample,"UV");
    var noise = NODE("SplitNode",-820,-400); LINK(sample,"RGBA",noise,"In");
    var noiseGain = OP2("MultiplyNode",noise,"B",null,null,-740,-400); SETIN(noiseGain,"B",0.4f);
    var noiseLevel = OP2("AddNode",noiseGain,"Out",null,null,-560,-400); SETIN(noiseLevel,"B",0.6f);
    object mask, core;
    if(isRing)
    {
        var uvSplit=NODE("SplitNode",-1650,0); LINK(uv,null,uvSplit,"In");
        var uv2=NODE("CombineNode",-1550,0); LINK(uvSplit,"R",uv2,"R"); LINK(uvSplit,"G",uv2,"G");
        var centered=OP2("SubtractNode",uv2,"RG",null,null,-1480,0); SETIN(centered,"B",new Vector4(.5f,.5f,0f,0f));
        var len=NODE("LengthNode",-1280,0); LINK(centered,"Out",len,"In");
        var radial=OP2("SubtractNode",len,"Out",null,null,-1080,0); SETIN(radial,"B",.44f);
        var abs=NODE("AbsoluteNode",-900,0); LINK(radial,"Out",abs,"In");
        var sm=NODE("SmoothstepNode",-700,0); SETIN(sm,"Edge1",.005f); SETIN(sm,"Edge2",.023f); LINK(abs,"Out",sm,"In");
        mask=NODE("OneMinusNode",-500,0); LINK(sm,"Out",mask,"In");
        var pow=NODE("PowerNode",-320,0); LINK(mask,"Out",pow,"A"); SETIN(pow,"B",4f); core=pow;
    }
    else
    {
        var fres=NODE("FresnelNode",-1260,100); SETIN(fres,"Power",1.6f);
        core=NODE("OneMinusNode",-1060,100); LINK(fres,"Out",core,"In");
        mask=noiseLevel;
    }
    var color=NODE("LerpNode",-100,200); LINK(red,null,color,"A"); LINK(white,null,color,"B"); LINK(core,"Out",color,"T");
    var colorFlow=OP2("MultiplyNode",color,"Out",noiseLevel,"Out",100,200);
    var threshold=NODE("SmoothstepNode",-320,-250); LINK(dissolve,null,threshold,"Edge1");
    var upper=OP2("AddNode",dissolve,null,null,null,-540,-650); SETIN(upper,"B",.18f); LINK(upper,"Out",threshold,"Edge2"); LINK(noiseLevel,"Out",threshold,"In");
    var alpha=OP2("MultiplyNode",mask,"Out",opacity,null,100,0);
    var erode=OP2("MultiplyNode",alpha,"Out",threshold,"Out",300,0);
    LINK(colorFlow,"Out",BLOCK("BaseColor"),null); LINK(erode,"Out",BLOCK("Alpha"),null);
    SAVE();
}
if (problems.Count > 0)
            {
                throw new System.InvalidOperationException(string.Join("\n", problems));
            }
        }
private static void BuildPrefabs()
{
    Material beam = MaterialAsset("M_BowLaserBeam", Root+"/Shaders/SG_BowLaserBeam.shadergraph");
    Material ring = MaterialAsset("M_BowLaserRing", Root+"/Shaders/SG_BowLaserRing.shadergraph");
    Material spark = AssetDatabase.LoadAssetAtPath<Material>("Assets/12.Materials/Player/M_BowLaserSpark.mat");
    if(spark==null)
    {
        spark=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        AssetDatabase.CreateAsset(spark,"Assets/12.Materials/Player/M_BowLaserSpark.mat");
    }
    spark.SetFloat("_Surface",1); spark.SetFloat("_Blend",2); spark.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha); spark.SetFloat("_DstBlend",(float)BlendMode.One);
    spark.SetTexture("_BaseMap", SparkTexture());
    spark.SetFloat("_ZWrite",0); spark.SetColor("_BaseColor",new Color(4f,4f,4f,1f));
    spark.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); spark.renderQueue=3000;
    EditorUtility.SetDirty(spark); AssetDatabase.SaveAssetIfDirty(spark);
    var cylinder=SaveMesh(Cylinder(false),"SM_BowLaserCylinder");
    var filamentMesh=SaveMesh(Cylinder(true),"SM_BowLaserFilament");
    var quad=SaveMesh(Quad(),"SM_BowLaserRingQuad");
    var flashMesh=SaveMesh(FlashMesh(),"SM_BowLaserMuzzleStar");
    var release=new GameObject("Player_BowLaserRelease");
    try
    {
        release.AddComponent<LocalPoolRoot>();
        var lifetime=release.AddComponent<PooledVisualLifetime>(); Set(lifetime,"_lifetimeSeconds",1.1f);
        var view=release.AddComponent<BowLaserVfxView>();
        var body=Renderer(release,"White core and red flowing sheath",cylinder,beam);
        var flash=Renderer(release,"White red muzzle star",flashMesh,beam);
        var filament=Renderer(release,"Wavering energy filament",filamentMesh,beam);
        var rings=new MeshRenderer[6];
        for(int i=0;i<rings.Length;i++)
                {
                    rings[i]=Renderer(release,"Expanding hoop "+i,quad,ring);
                }

                var particles=Particles(release,spark,56);
        var lightObject=new GameObject("Brief muzzle bounce"); lightObject.transform.SetParent(release.transform,false);
        var light=lightObject.AddComponent<Light>(); light.type=LightType.Point; light.range=12f; light.color=new Color(1f,.06f,.1f); light.shadows=LightShadows.None; light.intensity=0f;
        var so=new SerializedObject(view);
        so.FindProperty("_filament").objectReferenceValue=filament; so.FindProperty("_flash").objectReferenceValue=flash; so.FindProperty("_beam").objectReferenceValue=body; var rp=so.FindProperty("_rings"); rp.arraySize=rings.Length;
        for(int i=0;i<rings.Length;i++)
                {
                    rp.GetArrayElementAtIndex(i).objectReferenceValue=rings[i];
                }

                so.FindProperty("_sparks").objectReferenceValue=particles; so.FindProperty("_muzzleLight").objectReferenceValue=light; so.ApplyModifiedPropertiesWithoutUndo();
        view.ResetVisual(); PrefabUtility.SaveAsPrefabAsset(release,"Assets/02.Prefabs/Art/VFX/Player_BowLaserRelease.prefab");
    }
    finally { UnityEngine.Object.DestroyImmediate(release); }
    var charge=new GameObject("Player_BowCharge");
    try
    {
        charge.AddComponent<LocalPoolRoot>(); var lifetime=charge.AddComponent<PooledVisualLifetime>(); Set(lifetime,"_lifetimeSeconds",10f);
        var view=charge.AddComponent<BowChargeVfxView>();
        var core=Renderer(charge,"Charge focal point",cylinder,beam);
        var ps=Particles(charge,spark,24);
        var so=new SerializedObject(view); so.FindProperty("_core").objectReferenceValue=core; so.FindProperty("_particles").objectReferenceValue=ps; so.ApplyModifiedPropertiesWithoutUndo();
        view.ResetVisual(); PrefabUtility.SaveAsPrefabAsset(charge,"Assets/02.Prefabs/Art/VFX/Player_BowCharge.prefab");
    }
    finally { UnityEngine.Object.DestroyImmediate(charge); }
}
/// <summary>Updates only bow release energy assets, preserving tuned graph/ring settings.</summary>
public static void UpgradeEnergyDetail()
{
    const string prefabPath="Assets/02.Prefabs/Art/VFX/Player_BowLaserRelease.prefab";
    const string materialPath="Assets/12.Materials/Player/M_BowLaserSparkHead.mat";
    const string streakPath="Assets/12.Materials/Player/M_BowLaserReleaseSpark.mat";
    string texturePath=Root+"/T_BowLaserSparkHead.asset";
    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
    if(texture==null) { texture=new Texture2D(64,64,TextureFormat.RGBA32,false,true); texture.name="T_BowLaserSparkHead"; AssetDatabase.CreateAsset(texture,texturePath); }
    var pixels=new Color[4096];
    for(int y=0;y<64;y++)
            {
                for (int x=0;x<64;x++)
    {
        float r=new Vector2((x+.5f)/32f-1f,(y+.5f)/32f-1f).magnitude;
        float alpha=Mathf.Exp(-r*r*7f)*Mathf.Clamp01((1f-r)*6f);
        pixels[y*64+x]=new Color(1f,1f,1f,alpha);
    }
            }

            texture.SetPixels(pixels); texture.Apply(); texture.wrapMode=TextureWrapMode.Clamp; texture.filterMode=FilterMode.Bilinear;
    EditorUtility.SetDirty(texture); AssetDatabase.SaveAssetIfDirty(texture);
    var baseMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/12.Materials/Player/M_BowLaserSpark.mat");
    var headMaterial=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
    if(headMaterial==null) { headMaterial=new Material(baseMaterial); AssetDatabase.CreateAsset(headMaterial,materialPath); }
    headMaterial.SetTexture("_BaseMap",texture); headMaterial.SetColor("_BaseColor",new Color(18f,18f,18f,1f));
    EditorUtility.SetDirty(headMaterial); AssetDatabase.SaveAssetIfDirty(headMaterial);
    var streakMaterial=AssetDatabase.LoadAssetAtPath<Material>(streakPath);
    if(streakMaterial==null) { streakMaterial=new Material(baseMaterial); AssetDatabase.CreateAsset(streakMaterial,streakPath); }
    streakMaterial.SetColor("_BaseColor",new Color(8f,8f,8f,1f));
    EditorUtility.SetDirty(streakMaterial); AssetDatabase.SaveAssetIfDirty(streakMaterial);
    SaveMesh(Cylinder(true),"SM_BowLaserFilament");
    var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
    try
    {
        var view=prefab.GetComponent<BowLaserVfxView>(); var so=new SerializedObject(view);
        var head=so.FindProperty("_sparkHeads").objectReferenceValue as ParticleSystem;
        if(head==null) { head=Particles(prefab,headMaterial,56); head.name="Hot spark heads"; }
        var renderer=head.GetComponent<ParticleSystemRenderer>(); renderer.renderMode=ParticleSystemRenderMode.Billboard; renderer.alignment=ParticleSystemRenderSpace.View; renderer.sharedMaterial=headMaterial;
        so.FindProperty("_sparkHeads").objectReferenceValue=head;
        so.FindProperty("_sparkHeadSizeRange").vector2Value=new Vector2(.055f,.12f);
        so.FindProperty("_recoilFraction").floatValue=.42f;
        so.FindProperty("_extensionTime").floatValue=.07f;
        so.FindProperty("_tailDetachWindow").vector2Value=new Vector2(.55f,1.1f);
        so.FindProperty("_muzzleIntensity").floatValue=180f;
        var muzzle=so.FindProperty("_muzzleLight").objectReferenceValue as Light;
        if(muzzle!=null)
                {
                    muzzle.range=12f;
                }

                so.FindProperty("_beamCoreColor").colorValue=new Color(7f,2.8f,3.6f,1f);
        so.FindProperty("_beamEdgeColor").colorValue=new Color(4f,.06f,.18f,1f);
        so.FindProperty("_lateFilamentColor").colorValue=new Color(10f,6f,7f,1f);
        so.FindProperty("_lateFilamentFadeWindow").vector2Value=new Vector2(.48f,1.05f);
        var streaks=so.FindProperty("_sparks").objectReferenceValue as ParticleSystem;
        if(streaks!=null)
                {
                    streaks.GetComponent<ParticleSystemRenderer>().sharedMaterial=streakMaterial;
                }

                so.ApplyModifiedPropertiesWithoutUndo(); view.ResetVisual();
        PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
    }
    finally { PrefabUtility.UnloadPrefabContents(prefab); }
}
/// <summary>Replaces only the approved bow release visual assets with a short travelling arrow.</summary>
public static void UpgradeArrowDetail()
{
    const string prefabPath="Assets/02.Prefabs/Art/VFX/Player_BowLaserRelease.prefab";
    var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/12.Materials/Player/M_BowLaserBeam.mat");
    var sparkMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/12.Materials/Player/M_BowLaserReleaseSpark.mat");
    if(material==null || sparkMaterial==null)
            {
                throw new InvalidOperationException("Bow release base assets must exist before UpgradeArrowDetail.");
            }

            var tipMesh=SaveMesh(ArrowTipMesh(),"SM_BowArrowTip");
    var starMesh=SaveMesh(FlashMesh(),"SM_BowArrowTipStar");
    var arcMesh=SaveMesh(BrokenRingMesh(false),"SM_BowArrowBrokenRing");
    var nearArcMesh=SaveMesh(BrokenRingMesh(true),"SM_BowArrowNearRing");
    var threadMesh=SaveMesh(ArrowFiberMesh(9,false),"SM_BowArrowThread");
    var fibers=new Mesh[3]; for(int i=0;i<3;i++)
            {
                fibers[i]=SaveMesh(ArrowFiberMesh(i,true),"SM_BowArrowResidue"+i);
            }

            var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
    try
    {
        var view=prefab.GetComponent<BowLaserVfxView>(); var so=new SerializedObject(view);
        so.FindProperty("_duration").floatValue=.8f;
        so.FindProperty("_flightSpeed").floatValue=30f;
        so.FindProperty("_arrowTrailLength").floatValue=2.4f;
        so.FindProperty("_arrivalFade").floatValue=.08f;
        so.FindProperty("_goldStretchFactor").floatValue=1.5f;
        so.FindProperty("_sparkHeadSizeRange").vector2Value=new Vector2(.055f,.12f);
        so.FindProperty("_residueLifetime").floatValue=.22f;
        so.FindProperty("_ringFadeWindow").vector2Value=new Vector2(.12f,.43f);
        var tip=so.FindProperty("_arrowTip").objectReferenceValue as MeshRenderer;
        if(tip==null)
                {
                    tip =Renderer(prefab,"Thin travelling arrow point",tipMesh,material);
                }
                else
                {
                    tip.GetComponent<MeshFilter>().sharedMesh=tipMesh;
                }

                so.FindProperty("_arrowTip").objectReferenceValue=tip;
        var star=so.FindProperty("_tipStar").objectReferenceValue as MeshRenderer;
        if(star==null)
                {
                    star =Renderer(prefab,"Gold point starburst",starMesh,material);
                }
                else
                {
                    star.GetComponent<MeshFilter>().sharedMesh=starMesh;
                }

                so.FindProperty("_tipStar").objectReferenceValue=star;
        var filament=so.FindProperty("_filament").objectReferenceValue as MeshRenderer;
        if(filament!=null)
                {
                    filament.GetComponent<MeshFilter>().sharedMesh=threadMesh;
                }

                var rings=so.FindProperty("_rings");
        for(int i=0;i<rings.arraySize;i++)
        {
            var ring=rings.GetArrayElementAtIndex(i).objectReferenceValue as MeshRenderer;
            if(ring!=null)
                    {
                        ring.GetComponent<MeshFilter>().sharedMesh=i==0?nearArcMesh:arcMesh;
                    }
                }
        var residue=so.FindProperty("_residueFibers"); residue.arraySize=3;
        for(int i=0;i<3;i++)
        {
            var fiber=residue.GetArrayElementAtIndex(i).objectReferenceValue as MeshRenderer;
            if(fiber==null)
                    {
                        fiber =Renderer(prefab,"Broken residual fiber "+i,fibers[i],material);
                    }
                    else
                    {
                        fiber.GetComponent<MeshFilter>().sharedMesh=fibers[i];
                    }

                    residue.GetArrayElementAtIndex(i).objectReferenceValue=fiber;
        }
        var embers=so.FindProperty("_fineEmbers").objectReferenceValue as ParticleSystem;
        if(embers==null) { embers=Particles(prefab,sparkMaterial,48); embers.name="Fine bow and ring embers"; }
        var emitterRenderer=embers.GetComponent<ParticleSystemRenderer>(); emitterRenderer.velocityScale=.016f; emitterRenderer.lengthScale=1.5f;
        so.FindProperty("_fineEmbers").objectReferenceValue=embers;
        so.ApplyModifiedPropertiesWithoutUndo();
        Set(prefab.GetComponent<PooledVisualLifetime>(),"_lifetimeSeconds",.8f);
        view.ResetVisual(); PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
    }
    finally { PrefabUtility.UnloadPrefabContents(prefab); }
}
private static Mesh ArrowTipMesh()
{
    const int sides=4;
    var v=new Vector3[12]; var uv=new Vector2[12]; var tri=new int[48];
    for(int row=0;row<3;row++)
            {
                for (int i=0;i<sides;i++)
    {
        float z=row==0?0f:row==1?.22f:1f;
        float radius=row==0?.18f:row==1?1f:.005f;
        float angle=i*Mathf.PI*.5f+Mathf.PI*.25f;
        int n=row*sides+i; v[n]=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,z); uv[n]=new Vector2((float)i/sides,z);
    }
            }

            int k=0; for(int row=0;row<2;row++)
            {
                for (int i=0;i<sides;i++)
    { int a=row*sides+i,b=row*sides+(i+1)%sides,c=a+sides,d=b+sides; tri[k++]=a;tri[k++]=b;tri[k++]=c;tri[k++]=b;tri[k++]=d;tri[k++]=c; }
            }

            var mesh=new Mesh();mesh.vertices=v;mesh.uv=uv;mesh.triangles=tri;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
}
private static Mesh BrokenRingMesh(bool near)
{
    const int steps=192;
    var v=new Vector3[(steps+1)*2]; var uv=new Vector2[v.Length]; var tri=new System.Collections.Generic.List<int>();
    for(int i=0;i<=steps;i++)
    {
        float a=i*Mathf.PI*2f/steps;
        float roughPatch=Mathf.Clamp01((Mathf.Sin(a*3f+.4f)+Mathf.Sin(a*5f+1.1f))*.6f);
        float wrinkle=roughPatch*(.0045f*Mathf.Sin(i*.63f+Mathf.Sin(i*.17f))+.002f*Mathf.Sin(i*1.31f));
        for(int j=0;j<2;j++)
        { float baseRadius=.88f+(j==0?-.038f:.038f); float radius=baseRadius+wrinkle; int n=i*2+j; v[n]=new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0f); uv[n]=new Vector2(.5f+Mathf.Cos(a)*baseRadius*.5f,.5f+Mathf.Sin(a)*baseRadius*.5f); }
        bool gap=near ? (i==23 || i==24 || i==77 || i==121 || i==168 || i==169) : ((i>=16 && i<=18) || (i>=47 && i<=49) || i==69 || (i>=102 && i<=107) || (i>=138 && i<=141) || (i>=169 && i<=173));
        if(i==steps || gap)
                {
                    continue;
                }

                int b=i*2; tri.Add(b);tri.Add(b+1);tri.Add(b+2);tri.Add(b+1);tri.Add(b+3);tri.Add(b+2);
    }
    var mesh=new Mesh();mesh.vertices=v;mesh.uv=uv;mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
}
private static Mesh ArrowFiberMesh(int seed,bool residue)
{
    const int steps=112,sides=8;
    var v=new Vector3[(steps+1)*(sides+1)];var uv=new Vector2[v.Length];var tri=new System.Collections.Generic.List<int>();
    float phase=seed*1.913f;
    for(int i=0;i<=steps;i++)
    {
        float t=(float)i/steps,env=Mathf.Pow(Mathf.Max(0f,Mathf.Sin(t*Mathf.PI)),.7f);
        float detailScale=residue ? .42f*(.88f+seed*.11f) : .68f;
        float ramp=(12f*t+43f*t*t)*detailScale;
        float x=(Mathf.Sin(ramp+phase)*1.7f+Mathf.Sin(t*37f*detailScale+phase*2f)*.65f+Mathf.Sin(t*97f*detailScale+phase)*.19f)*env;
        float y=(Mathf.Sin(ramp*.73f+phase+1f)*1.9f+Mathf.Sin(t*51f*detailScale+phase)*.45f)*env;
        float branchStart=seed==0?0f:seed==1?.12f:.55f;
        float branchEnd=seed==0?1f:seed==1?.58f:.93f;
        if(residue)
        {
            float u=Mathf.Clamp01((t-branchStart)/(branchEnd-branchStart));
            float branchEnvelope=Mathf.Sin(u*Mathf.PI);
            x=env*(Mathf.Sin(t*10.3f+.3f)*.82f+Mathf.Sin(t*22f+.7f)*.11f);
            y=x*.24f+env*Mathf.Sin(t*16f+.9f)*.12f;
            if(seed!=0) { x+=branchEnvelope*.18f*Mathf.Sin(u*4.2f+phase); y+=branchEnvelope*(seed==1?.56f:-.43f); }
            env*=branchEnvelope;
        }
        float width=(residue ? (seed==0?.32f:seed==1?.21f:.17f) : .4f)*env*(.45f+.55f*Mathf.Abs(Mathf.Sin(t*19f+phase)));
        if(!residue) { x*=.65f; y*=.65f; }
        for(int j=0;j<=sides;j++)
        {
            float a=j*Mathf.PI*2f/sides;int n=i*(sides+1)+j;
            v[n]=new Vector3(x+Mathf.Cos(a)*width,y+Mathf.Sin(a)*width,t);uv[n]=new Vector2((float)j/sides,t);
        }
        if(i==steps || (residue && (t<branchStart || t>=branchEnd || (i+seed*11)%43>=41 || (i>70 && (i+seed*7)%29==27))))
                {
                    continue;
                }

                for (int j=0;j<sides;j++) { int b=i*(sides+1)+j;tri.Add(b);tri.Add(b+1);tri.Add(b+sides+1);tri.Add(b+1);tri.Add(b+sides+2);tri.Add(b+sides+1); }
    }
    var mesh=new Mesh();mesh.vertices=v;mesh.uv=uv;mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
}
private static Texture2D SparkTexture()
{
    string path=Root+"/T_BowLaserSpark.asset";
    var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    if(texture==null) { texture=new Texture2D(64,64,TextureFormat.RGBA32,false,true); texture.name="T_BowLaserSpark"; AssetDatabase.CreateAsset(texture,path); }
    var pixels=new Color[64*64];
    for(int y=0;y<64;y++)
            {
                for (int x=0;x<64;x++)
    {
        float r=new Vector2((x+.5f)/32f-1f,(y+.5f)/32f-1f).magnitude;
        float alpha=Mathf.Pow(Mathf.Clamp01(1f-r),1.2f);
        float core=Mathf.Pow(Mathf.Clamp01(1f-r*2.8f),.65f);
        var color=Color.white; color.a=alpha*(.55f+.45f*core);
        pixels[y*64+x]=color;
    }
            }

            texture.SetPixels(pixels); texture.Apply(); texture.wrapMode=TextureWrapMode.Clamp; texture.filterMode=FilterMode.Bilinear;
    EditorUtility.SetDirty(texture); AssetDatabase.SaveAssetIfDirty(texture); return texture;
}
private static Material MaterialAsset(string name,string shaderPath)
{
 var shader=AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
 if(shader==null)
            {
                throw new InvalidOperationException("Shader import missing: "+shaderPath);
            }

            string path="Assets/12.Materials/Player/"+name+".mat";
 var material=AssetDatabase.LoadAssetAtPath<Material>(path);
 if(material==null) { material=new Material(shader); AssetDatabase.CreateAsset(material,path); }
 else
            {
                material.shader=shader;
            }

            material.SetColor("_CoreColor",new Color(3.8f,.85f,1.2f,1f)); material.SetColor("_EdgeColor",new Color(2.8f,.025f,.12f,1f));
 material.SetFloat("_Opacity",1f); material.SetFloat("_Dissolve",0f);
 EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); return material;
}
private static void Set(UnityEngine.Object target,string key,float value)
{ var so=new SerializedObject(target); so.FindProperty(key).floatValue=value; so.ApplyModifiedPropertiesWithoutUndo(); }
private static MeshRenderer Renderer(GameObject parent,string name,Mesh mesh,Material material)
{
 var go=new GameObject(name); go.transform.SetParent(parent.transform,false);
 go.AddComponent<MeshFilter>().sharedMesh=mesh;
 var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false; renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off; return renderer;
}
private static ParticleSystem Particles(GameObject parent,Material material,int capacity)
{
 var go=new GameObject("Velocity stretched sparks"); go.transform.SetParent(parent.transform,false);
 var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
 var main=ps.main; main.playOnAwake=false; main.loop=false; main.simulationSpace=ParticleSystemSimulationSpace.Local; main.maxParticles=capacity; main.startSpeed=0f;
 var emission=ps.emission; emission.enabled=false; var shape=ps.shape; shape.enabled=false;
 ps.useAutoRandomSeed=false; ps.randomSeed=127;
 var renderer=ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material; renderer.renderMode=ParticleSystemRenderMode.Stretch; renderer.velocityScale=.036f; renderer.lengthScale=3f; renderer.cameraVelocityScale=0f; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
 return ps;
}
private static Mesh SaveMesh(Mesh mesh,string name)
{
 string path=Root+"/"+name+".asset"; mesh.name=name;
 var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
 if(existing==null) { AssetDatabase.CreateAsset(mesh,path); return mesh; }
 // Assign through Mesh APIs so an already loaded renderer receives fresh native buffers too.
 existing.Clear(false);
 existing.indexFormat=mesh.indexFormat;
 existing.name=mesh.name;
 existing.vertices=mesh.vertices;
 if(mesh.HasVertexAttribute(VertexAttribute.Normal))
            {
                existing.normals=mesh.normals;
            }

            if (mesh.HasVertexAttribute(VertexAttribute.Tangent))
            {
                existing.tangents=mesh.tangents;
            }

            if (mesh.HasVertexAttribute(VertexAttribute.Color))
            {
                existing.colors=mesh.colors;
            }

            for (int channel=0;channel<8;channel++)
 {
     var attribute=(VertexAttribute)((int)VertexAttribute.TexCoord0+channel);
     if(!mesh.HasVertexAttribute(attribute))
                {
                    continue;
                }

                int dimensions=mesh.GetVertexAttributeDimension(attribute);
     if(dimensions==2) { var values=new System.Collections.Generic.List<Vector2>(); mesh.GetUVs(channel,values); existing.SetUVs(channel,values); }
     else if(dimensions==3) { var values=new System.Collections.Generic.List<Vector3>(); mesh.GetUVs(channel,values); existing.SetUVs(channel,values); }
     else { var values=new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(channel,values); existing.SetUVs(channel,values); }
 }
 existing.subMeshCount=mesh.subMeshCount;
 for(int submesh=0;submesh<mesh.subMeshCount;submesh++)
            {
                existing.SetIndices(mesh.GetIndices(submesh),mesh.GetTopology(submesh),submesh,false);
            }

            existing.bounds=mesh.bounds;
 existing.UploadMeshData(false);
 UnityEngine.Object.DestroyImmediate(mesh);
 EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); return existing;
}
private static Mesh FlashMesh()
{
 const int rays=11;
 var vertices=new Vector3[rays*6]; var uv=new Vector2[vertices.Length]; var triangles=new int[rays*6];
 for(int i=0;i<rays;i++)
 {
    float a=i*2.399963f; var direction=new Vector3(Mathf.Cos(a),Mathf.Sin(a),Mathf.Sin(i*7f)*.55f).normalized;
    var side=Vector3.Cross(direction,Vector3.forward).normalized * .045f;
    int b=i*6,k=i*6; float length=.55f+Mathf.Repeat(i*.618f,1f)*.75f;
    vertices[b]=-side; vertices[b+1]=side; vertices[b+2]=direction*length;
    vertices[b+3]=new Vector3(0,0,-.045f); vertices[b+4]=new Vector3(0,0,.045f); vertices[b+5]=direction*length;
    uv[b]=Vector2.zero; uv[b+1]=Vector2.right; uv[b+2]=new Vector2(.5f,1f); uv[b+3]=Vector2.zero; uv[b+4]=Vector2.right; uv[b+5]=new Vector2(.5f,1f);
    triangles[k]=b; triangles[k+1]=b+1; triangles[k+2]=b+2; triangles[k+3]=b+3; triangles[k+4]=b+4; triangles[k+5]=b+5;
 }
 var mesh=new Mesh(); mesh.vertices=vertices; mesh.uv=uv; mesh.triangles=triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
}
private static Mesh Quad()
{
 var m=new Mesh(); m.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
 m.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)}; m.triangles=new[]{0,1,2,0,2,3}; m.RecalculateNormals(); m.RecalculateBounds(); return m;
}
private static Mesh Cylinder(bool filament)
{
 const int sides=24, steps=48;
 var v=new Vector3[(sides+1)*(steps+1)]; var uv=new Vector2[v.Length]; var tri=new int[sides*steps*6];
 for(int z=0;z<=steps;z++)
            {
                for (int s=0;s<=sides;s++)
 {
    float t=(float)z/steps,a=s*Mathf.PI*2/sides; int i=z*(sides+1)+s;
    float taper=Mathf.Min(1f, Mathf.Min(t*40f+.04f,(1f-t)*24f+.02f));
    float width=filament ? .6f : 1f;
    float envelope=Mathf.Sin(t*Mathf.PI);
    float wobbleX=envelope*(filament ? Mathf.Sin(t*11f+.3f)*8f+Mathf.Sin(t*23f)*2f : Mathf.Sin(t*63f)*1.65f+Mathf.Sin(t*117f)*.75f);
    float wobbleY=envelope*(filament ? Mathf.Sin(t*7.5f+.8f)*8f+Mathf.Sin(t*19f)*2.5f : Mathf.Sin(t*47f+.8f)*1.35f+Mathf.Sin(t*103f)*.6f);
    v[i]=new Vector3(Mathf.Cos(a)*taper*width + wobbleX*(filament ? 1f : .08f),Mathf.Sin(a)*taper*width + wobbleY*(filament ? 1f : .08f),t); uv[i]=new Vector2((float)s/sides,t);
 }
            }

            int k=0; for(int z=0;z<steps;z++)
            {
                for (int s=0;s<sides;s++)
 { int i=z*(sides+1)+s; tri[k++]=i; tri[k++]=i+1; tri[k++]=i+sides+1; tri[k++]=i+1; tri[k++]=i+sides+2; tri[k++]=i+sides+1; }
            }

            var mesh=new Mesh(); mesh.vertices=v; mesh.uv=uv; mesh.triangles=tri; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
}
}
}
