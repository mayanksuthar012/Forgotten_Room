using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single interactive button on the door num pad (0-9, CLEAR, OK).
/// Implements IInteractable so both VR controller laser rays and desktop raycasts can interact with it.
/// </summary>
public class KeypadButton : MonoBehaviour, IInteractable
{
    private DoorKeypad keypad;
    private string buttonValue = "";
    private Image buttonImage;
    private Color normalColor;
    private Color hoverColor;
    private Color clickColor;
    private bool isHovered = false;

    public string ButtonValue => buttonValue;

    public string interactionText
    {
        get
        {
            if (buttonValue == "CLEAR") return "Clear PIN";
            if (buttonValue == "OK") return "Submit PIN";
            return $"Press '{buttonValue}'";
        }
    }

    public void Setup(DoorKeypad parentKeypad, string value, Color bgColor, Color glowColor)
    {
        keypad = parentKeypad;
        buttonValue = value;
        buttonImage = GetComponent<Image>();
        normalColor = bgColor;
        hoverColor = glowColor;
        clickColor = Color.white;

        if (buttonImage != null)
        {
            buttonImage.color = normalColor;
        }
    }

    public void Interact()
    {
        if (keypad != null)
        {
            keypad.OnButtonPressed(buttonValue);
            Ghost.EmitSound(transform.position, 18f, $"Keypad Press '{buttonValue}'");
            StartCoroutine(FlashClickRoutine());
        }
    }

    private IEnumerator FlashClickRoutine()
    {
        if (buttonImage != null) buttonImage.color = clickColor;
        yield return new WaitForSeconds(0.12f);
        if (buttonImage != null) buttonImage.color = isHovered ? hoverColor : normalColor;
    }

    private int hoverCount = 0;

    public void SetHover(bool hover)
    {
        if (hover) hoverCount++;
        else hoverCount = Mathf.Max(0, hoverCount - 1);

        bool shouldBeHovered = hoverCount > 0;
        if (isHovered == shouldBeHovered) return;
        isHovered = shouldBeHovered;
        if (buttonImage != null)
        {
            buttonImage.color = isHovered ? hoverColor : normalColor;
        }
    }
}
