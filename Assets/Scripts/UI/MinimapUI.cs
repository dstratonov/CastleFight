using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MinimapUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Camera minimapCamera;
    [SerializeField] private RawImage minimapImage;
    [SerializeField] private RectTransform minimapRect;
    [SerializeField] private RTSCameraController cameraController;

    [Header("Bounds")]
    [SerializeField] private float mapMinX = -50f;
    [SerializeField] private float mapMaxX = 50f;
    [SerializeField] private float mapMinZ = -50f;
    [SerializeField] private float mapMaxZ = 50f;

    private void Start()
    {
        if (minimapCamera != null)
        {
            var rt = new RenderTexture(256, 256, 16);
            minimapCamera.targetTexture = rt;
            if (minimapImage != null)
                minimapImage.texture = rt;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (minimapRect == null || cameraController == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            minimapRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        Vector2 normalizedPoint = new(
            (localPoint.x / minimapRect.rect.width) + 0.5f,
            (localPoint.y / minimapRect.rect.height) + 0.5f
        );

        float worldX = Mathf.Lerp(mapMinX, mapMaxX, normalizedPoint.x);
        float worldZ = Mathf.Lerp(mapMinZ, mapMaxZ, normalizedPoint.y);

        cameraController.FocusOn(new Vector3(worldX, 0, worldZ));
    }
}
