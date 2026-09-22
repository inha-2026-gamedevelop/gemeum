using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace Geumeum.Combat
{
    [BlackboardEnum]
    public enum PlayerBehaviorStep
    {
        Blocked = 0, Human = 1, PrepareBot = 2,
        Safety = 3, Defend = 4, Support = 5, Combat = 6, Follow = 7, Idle = 8, Regroup = 9,
    }

    /// <summary>Unity Behavior leaf. Graph flow nodes own priority; the existing server APIs own actions</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Player Decision", story: "[Step]", category: "Geumeum/Player",
        id: "c5aa6f61dc804aab80d96d047b8e52a0")]
    public partial class PlayerBehaviorAction : Unity.Behavior.Action
    {
        [SerializeReference] public BlackboardVariable<PlayerBehaviorStep> Step = new();
        [NonSerialized] private PlayerController _controller;

        protected override void OnSetup()
        {
            _controller = GameObject != null ? GameObject.GetComponent<PlayerController>() : null;
        }

        protected override Status OnStart()
        {
            return _controller != null && _controller.ServerBrain != null
                ? _controller.ServerBrain.EvaluateBehaviorStep(Step.Value) : Status.Failure;
        }

        protected override void OnTeardown() { _controller = null; }
    }
}
