using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Main controller for the dice-based volume system.
/// Manages two dice and sets the master volume (GameSettings.MasterVolume) from their total.
/// Starts from the saved volume; a roll never goes below minimumVolume, so the game can't be muted by luck.
/// </summary>
public class VolumeSettingsDice : MonoBehaviour
{
    [Header("Dice References")]
    public VolumeDie die1;
    public VolumeDie die2;

    [Header("Throw Settings")]
    public float throwForce = 8f;
    public float throwTorque = 15f;
    public Transform throwOrigin;
    public float settleTime = 2f;

    [Header("Volume")]
    [Tooltip("Lowest master volume a roll can set (0-1)")]
    [Range(0f, 1f)]
    public float minimumVolume = 0.1f;

    [Header("UI References")]
    public GameObject volumeMeterPanel;
    public Image[] volumeBars; // 10 bars for volume display
    public TextMeshProUGUI volumeNumberText;
    public TextMeshProUGUI statusText;
    public Button throwButton;
    public Button resetButton;

    [Header("Colors")]
    public Color normalBarColor = new Color(0.3f, 0.7f, 0.3f);
    public Color highBarColor = new Color(0.9f, 0.7f, 0.2f);
    public Color overloadColor = new Color(0.9f, 0.2f, 0.2f);
    public Color emptyBarColor = new Color(0.2f, 0.2f, 0.2f);

    [Header("Audio")]
    public AudioSource diceRollSound;
    public AudioSource diceHitSound;

    private bool isRolling = false;
    private int currentVolume = 7; // Dice total (2-12) shown on the meter

    void Start()
    {
        if (throwButton != null)
            throwButton.onClick.AddListener(ThrowDice);
        if (resetButton != null)
            resetButton.onClick.AddListener(ResetDice);

        // Show the saved volume (nothing is changed or saved here)
        ShowSavedVolume();
        if (statusText != null)
            statusText.text = $"Volume: {Mathf.RoundToInt(GameSettings.MasterVolume * 100f)}%";

        // Keep the meter in step when the settings slider changes the volume
        GameSettings.Changed += OnSettingsChanged;

        if (die1 == null || die2 == null)
            Debug.LogWarning("[VolumeSettingsDice] A die is not assigned - its roll will be simulated.");
    }

    void OnDestroy()
    {
        GameSettings.Changed -= OnSettingsChanged;
    }

    void OnSettingsChanged()
    {
        if (!isRolling)
            ShowSavedVolume();
    }

    void ShowSavedVolume()
    {
        currentVolume = DiceTotalForVolume(GameSettings.MasterVolume);
        UpdateVolumeDisplay(currentVolume);
    }

    public void ThrowDice()
    {
        if (isRolling || !isActiveAndEnabled) return;

        StartCoroutine(ThrowDiceRoutine());
    }

    IEnumerator ThrowDiceRoutine()
    {
        isRolling = true;

        if (throwButton != null)
            throwButton.interactable = false;

        if (statusText != null)
            statusText.text = "Rolling...";

        // Play roll sound
        if (diceRollSound != null)
        {
            diceRollSound.volume = GameSettings.SfxVolume;
            diceRollSound.Play();
        }

        // Reset dice positions
        ResetDicePositions();
        yield return new WaitForSeconds(0.1f);

        // Throw both dice
        ThrowSingleDie(die1);
        ThrowSingleDie(die2);

        // Wait for dice to settle
        yield return new WaitForSeconds(settleTime);

        // Keep checking until both dice are settled
        float timeout = 5f;
        float elapsed = 0f;
        while ((!IsDieSettled(die1) || !IsDieSettled(die2)) && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.2f);
            elapsed += 0.2f;
        }

        // Get results
        int value1 = GetDieValue(die1);
        int value2 = GetDieValue(die2);
        int total = value1 + value2;

        // Update volume
        currentVolume = total;
        ApplyVolume(total);
        UpdateVolumeDisplay(total);

        // Update status
        if (statusText != null)
        {
            int percent = Mathf.RoundToInt(VolumeForDiceTotal(total) * 100f);
            if (total > 10)
                statusText.text = $"LOUD! ({value1} + {value2} = {total}) - {percent}%";
            else if (total <= 3)
                statusText.text = $"Quiet... ({value1} + {value2} = {total}) - {percent}%";
            else
                statusText.text = $"Volume: {value1} + {value2} = {total} - {percent}%";
        }

        isRolling = false;

        if (throwButton != null)
            throwButton.interactable = true;
    }

    static bool IsDieSettled(VolumeDie die)
    {
        return die == null || die.IsSettled();
    }

    /// <summary>A missing die rolls "in the air" so the mechanic keeps working.</summary>
    static int GetDieValue(VolumeDie die)
    {
        return die != null ? Mathf.Clamp(die.GetTopFaceValue(), 1, 6) : Random.Range(1, 7);
    }

    void ThrowSingleDie(VolumeDie die)
    {
        if (die == null) return;

        Rigidbody rb = die.GetComponent<Rigidbody>();
        if (rb == null) return;

        // Reset velocity
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Random throw direction
        Vector3 throwDir = new Vector3(
            Random.Range(-0.3f, 0.3f),
            Random.Range(0.5f, 1f),
            Random.Range(-0.3f, 0.3f)
        ).normalized;

        // Apply force
        rb.AddForce(throwDir * throwForce, ForceMode.Impulse);

        // Random torque for spinning
        Vector3 torque = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ) * throwTorque;

        rb.AddTorque(torque, ForceMode.Impulse);
    }

    void ResetDicePositions()
    {
        if (throwOrigin == null)
        {
            // Use default positions above the table
            if (die1 != null)
            {
                die1.transform.position = new Vector3(-0.5f, 2f, 0);
                die1.transform.rotation = Random.rotation;
            }
            if (die2 != null)
            {
                die2.transform.position = new Vector3(0.5f, 2f, 0);
                die2.transform.rotation = Random.rotation;
            }
        }
        else
        {
            if (die1 != null)
            {
                die1.transform.position = throwOrigin.position + Vector3.left * 0.3f;
                die1.transform.rotation = Random.rotation;
            }
            if (die2 != null)
            {
                die2.transform.position = throwOrigin.position + Vector3.right * 0.3f;
                die2.transform.rotation = Random.rotation;
            }
        }

        // Reset rigidbodies
        if (die1 != null)
        {
            Rigidbody rb1 = die1.GetComponent<Rigidbody>();
            if (rb1 != null) { rb1.linearVelocity = Vector3.zero; rb1.angularVelocity = Vector3.zero; }
        }
        if (die2 != null)
        {
            Rigidbody rb2 = die2.GetComponent<Rigidbody>();
            if (rb2 != null) { rb2.linearVelocity = Vector3.zero; rb2.angularVelocity = Vector3.zero; }
        }
    }

    public void ResetDice()
    {
        if (isRolling) return;
        ResetDicePositions();

        if (statusText != null)
            statusText.text = "Click 'Roll' to set volume";
    }

    /// <summary>Dice total (2-12) to master volume: 2 = 0%, 12 = 100%, never below minimumVolume.</summary>
    float VolumeForDiceTotal(int diceTotal)
    {
        float volume = Mathf.Clamp01((diceTotal - 2) / 10f);
        return Mathf.Max(Mathf.Clamp01(minimumVolume), volume);
    }

    /// <summary>The dice total that shows a given volume on the meter.</summary>
    static int DiceTotalForVolume(float volume)
    {
        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(volume) * 10f) + 2, 2, 12);
    }

    void ApplyVolume(int diceTotal)
    {
        // Applies AudioListener.volume, saves it, and updates the other settings screens
        GameSettings.MasterVolume = VolumeForDiceTotal(diceTotal);
        GameSettings.Save();
    }

    void UpdateVolumeDisplay(int diceTotal)
    {
        if (volumeNumberText != null)
        {
            volumeNumberText.text = diceTotal.ToString();

            if (diceTotal > 10)
            {
                volumeNumberText.color = overloadColor;
                volumeNumberText.fontStyle = FontStyles.Bold;
            }
            else
            {
                volumeNumberText.color = Color.white;
                volumeNumberText.fontStyle = FontStyles.Normal;
            }
        }

        // Update volume bars
        if (volumeBars != null && volumeBars.Length > 0)
        {
            for (int i = 0; i < volumeBars.Length; i++)
            {
                if (volumeBars[i] == null) continue;

                int barThreshold = i + 1; // Bar 0 = level 1, Bar 9 = level 10

                // Adjust for dice range (2-12) -> bar range (1-10)
                // Dice 2 = bar 0, Dice 11 = bar 9, Dice 12 = all bars + overload
                int adjustedDice = diceTotal - 1; // Convert 2-12 to 1-11

                if (adjustedDice >= barThreshold)
                {
                    // Bar is filled
                    if (diceTotal > 10)
                    {
                        // Overload - all bars turn red
                        volumeBars[i].color = overloadColor;
                    }
                    else if (adjustedDice >= 8)
                    {
                        // High volume - yellow/orange
                        volumeBars[i].color = highBarColor;
                    }
                    else
                    {
                        // Normal volume - green
                        volumeBars[i].color = normalBarColor;
                    }
                }
                else
                {
                    // Bar is empty
                    volumeBars[i].color = emptyBarColor;
                }
            }
        }
    }

    void OnValidate()
    {
        // Preview only while playing: changing other objects here in edit mode dirtied the scene on every recompile
        if (!Application.isPlaying) return;
        if (volumeBars != null && volumeBars.Length > 0)
        {
            UpdateVolumeDisplay(currentVolume);
        }
    }
}
