using UnityEngine;

public class NodeRecalcScheduler : MonoBehaviour
{
    [SerializeField] private int intervalFrames = 10;

    private void Update()
    {
        if (intervalFrames <= 1)
        {
            MapNode.ProcessRecalculateStartDistanceIfNeeded();
            return;
        }

        // 10ƒtƒŒ[ƒ€‚É1‰ñ‚¾‚¯
        if (Time.frameCount % intervalFrames != 0) return;

        MapNode.ProcessRecalculateStartDistanceIfNeeded();
    }
}
