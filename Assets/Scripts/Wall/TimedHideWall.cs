using System.Collections;
using UnityEngine;

public class TimedHideWall : MonoBehaviour
{
    [Header("開始してから何秒後に消すか")]
    public float hideAfterSeconds = 2f;

    [Header("復活させるなら true（消したままなら false）")]
    public bool respawn = true;

    [Header("消えている時間（respawn=true のときだけ有効）")]
    public float hiddenDurationSeconds = 3f;

    [Header("Start時に自動実行する")]
    public bool autoStart = true;

    Coroutine routine;

    void Start()
    {
        if (autoStart) Trigger();
    }

    // 外部から呼べるように公開
    public void Trigger()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(HideRoutine());
    }

    IEnumerator HideRoutine()
    {
        yield return new WaitForSeconds(hideAfterSeconds);

        gameObject.SetActive(false);

        if (!respawn) yield break;

        yield return new WaitForSeconds(hiddenDurationSeconds);

        gameObject.SetActive(true);
    }
}
