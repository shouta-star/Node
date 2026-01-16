using UnityEngine;

public class GameTimeOverlay : MonoBehaviour
{
    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = 32;
        style.normal.textColor = Color.white;

        // îwåiÅiçïÇ¢î†Åj
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.Box(new Rect(10, 10, 320, 60), GUIContent.none);

        // ï∂éö
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 20, 300, 40), $"Time: {Time.time:0.00}s", style);
    }
}
