using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.Game
{
    public sealed partial class EnemyFigure : MonoBehaviour
    {
        /// <summary>楕円体。球を 3 軸別々に伸ばして、角の無い塊を作る。</summary>
        static void Blob(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            Paint(go, mat);
        }

        /// <summary>断面が楕円のカプセル。側面がまっすぐなので胴や腰に向く。</summary>
        static void CapsuleShaped(string name, Transform parent, Vector3 pos,
            float length, float radiusX, float radiusZ, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(radiusX * 2f, length * 0.5f, radiusZ * 2f);
            Paint(go, mat);
        }

        static void Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            Paint(go, mat);
        }

        static void Sphere(string name, Transform parent, Vector3 pos, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * (radius * 2f);
            Paint(go, mat);
        }

        static void Capsule(string name, Transform parent, Vector3 pos, float length, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            Paint(go, mat);
        }

        static void NoCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null)
                return;
            // 見た目用のパーツが当たり判定を持ると、移動や被弾の判定に混ざる
            if (Application.isPlaying)
                Object.Destroy(collider);
            else
                Object.DestroyImmediate(collider);
        }

        static void Paint(GameObject go, Material mat)
        {
            if (mat == null)
                return;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = mat;
        }
    }
}
