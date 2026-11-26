using UnityEngine;

// Map data licence notice required by the circuit pack: "(c) OpenStreetMap contributors" (ODbL),
// shown small in the corner whenever a circuit is on screen.
public class MapCredit : MonoBehaviour
{
    public const string Text = "Map data © OpenStreetMap contributors (ODbL)";
    GUIStyle style;

    void OnGUI()
    {
        float s = Screen.height / 1080f;
        if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight };
        style.fontSize = Mathf.Max(10, Mathf.RoundToInt(13 * s));
        style.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        GUI.depth = -10;
        GUI.Label(new Rect(Screen.width - 420 * s, Screen.height - 22 * s, 410 * s, 20 * s), Text, style);
    }
}
