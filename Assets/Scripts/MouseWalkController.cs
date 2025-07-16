using UnityEngine;
using System.Collections.Generic;

public class MouseWalkController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float stepDistance = 1f;
    public float stepSpeed = 5f;
    public float returnSpeed = 3f;
    public float maxZPosition = 10f; // Maximum Z position limit

    [Header("Descent Control")]
    public float descentSpeed = 2f; // Speed of automatic descent
    public float resistanceStrength = 1f; // How much left/right input affects descent
    public float maxSidewaysDeviation = 3f; // Maximum distance you can go left/right during descent
    public float resistanceSlowdown = 0.5f; // How much resistance slows descent (0 = no slowdown, 1 = full stop)
    public float resistanceDecay = 2f; // How quickly resistance effect fades

    [Header("Progressive Difficulty")]
    [Range(0.1f, 1f)]
    public float minForwardSpeed = 0.2f; // Minimum speed multiplier when near max Z
    [Range(1f, 5f)]
    public float maxReturnSpeedMultiplier = 3f; // Maximum return speed multiplier when far from initial position
    public AnimationCurve difficultycurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f); // Curve for forward speed falloff
    public AnimationCurve returnCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 1f); // Curve for return speed increase

    [Header("Rhythm Settings")]
    public float idealBeatInterval = 0.5f; // Ideal time between clicks (in seconds)
    public float rhythmTolerance = 0.15f; // How much deviation is allowed from ideal beat
    public float rhythmBufferTime = 2f; // Time window to establish rhythm
    public int minBeatsForRhythm = 3; // Minimum beats needed to establish rhythm
    public bool showRhythmDebug = false; // Show rhythm feedback in console

    [Header("Animation")]
    public Animator animator;
    public string speedParameterName = "Speed";

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip stepSound;
    public float minPitch = 0.8f;
    public float maxPitch = 1.2f;

    [Header("Debug Info")]
    public bool lastStepWasLeft = false; // Shows which button should be pressed next

    private Vector3 initialPosition;
    private Vector3 targetPosition;
    private bool isMovingToTarget = false;
    private bool isReturning = false;
    private bool isDescending = false; // New state for automatic descent
    private Vector3 descentStartPosition; // Where descent began

    // Movement state enum for clarity
    public enum MovementState
    {
        Idle,
        MovingForward,
        Descending,
        Returning
    }
    public MovementState currentState = MovementState.Idle;

    // Rhythm tracking variables
    private float lastClickTime = 0f;
    private List<float> clickIntervals = new List<float>();
    private float establishedRhythm = 0f;
    private bool hasEstablishedRhythm = false;
    private float rhythmConfidence = 0f;

    // Descent resistance tracking
    private float currentResistanceEffect = 0f; // Current resistance slowdown multiplier

    void Start()
    {
        // Store the initial position when the script starts
        initialPosition = transform.position;
        targetPosition = transform.position;

        // Get animator component if not assigned
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        // Get audio source component if not assigned
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        // Set initial animation speed to 0
        if (animator != null)
        {
            animator.SetFloat(speedParameterName, 0f);
        }
    }

    void Update()
    {
        // Check for mouse button presses (not holds)
        bool leftMouseDown = Input.GetMouseButtonDown(0);
        bool rightMouseDown = Input.GetMouseButtonDown(1);

        // Handle different movement states
        switch (currentState)
        {
            case MovementState.Idle:
            case MovementState.MovingForward:
                HandleForwardMovement(leftMouseDown, rightMouseDown);
                break;

            case MovementState.Descending:
                HandleDescentResistance(leftMouseDown, rightMouseDown);
                break;

            case MovementState.Returning:
                HandleDescentResistance(leftMouseDown, rightMouseDown); // Apply resistance during return too
                break;
        }

        // Handle movement execution
        if (isMovingToTarget)
        {
            MoveTowardsTarget();
        }
        else if (isDescending)
        {
            HandleDescent();
        }
        else if (isReturning)
        {
            ReturnToInitialPosition();
        }
        else if (currentState == MovementState.Idle)
        {
            // Check if we should start returning (only when not at max Z)
            if (Vector3.Distance(transform.position, initialPosition) > 0.1f && !HasReachedMaxZ())
            {
                StartReturning();
            }
        }
    }

    // Handle forward movement input (normal stepping)
    void HandleForwardMovement(bool leftMouseDown, bool rightMouseDown)
    {
        if (leftMouseDown || rightMouseDown)
        {
            float currentTime = Time.time;

            // Play step sound regardless of whether step is valid
            PlayStepSound();

            // Check if this is the correct button (alternating pattern)
            bool correctButton = false;
            if (leftMouseDown && !lastStepWasLeft)
            {
                correctButton = true;
            }
            else if (rightMouseDown && lastStepWasLeft)
            {
                correctButton = true;
            }

            // Check if this click fits the rhythm
            bool correctTiming = IsValidRhythmStep(currentTime);

            // Step is valid only if BOTH button alternation AND timing are correct
            bool validStep = correctButton && correctTiming;

            if (validStep)
            {
                TakeStep();
                lastStepWasLeft = leftMouseDown; // Update which button was pressed
            }
            else if (showRhythmDebug)
            {
                if (!correctButton)
                    Debug.Log("Step rejected - wrong button (need " + (lastStepWasLeft ? "right" : "left") + ")");
                if (!correctTiming)
                    Debug.Log("Step rejected - rhythm off");
            }

            // Update rhythm tracking only for correct button presses
            if (correctButton)
            {
                UpdateRhythmTracking(currentTime);
            }
        }
    }

    // Handle resistance input during descent
    void HandleDescentResistance(bool leftMouseDown, bool rightMouseDown)
    {
        if (leftMouseDown || rightMouseDown)
        {
            // Play step sound for resistance attempts
            PlayStepSound();

            // Apply resistance effect (slows descent)
            currentResistanceEffect = Mathf.Min(currentResistanceEffect + resistanceSlowdown, 1f);

            // Calculate sideways movement based on button pressed
            Vector3 sidewaysDirection = Vector3.zero;
            if (leftMouseDown)
            {
                sidewaysDirection = -transform.right * resistanceStrength;
            }
            else if (rightMouseDown)
            {
                sidewaysDirection = transform.right * resistanceStrength;
            }

            // Check if sideways movement would exceed maximum deviation
            Vector3 potentialPosition = transform.position + sidewaysDirection;
            Vector3 sidewaysOffset = potentialPosition - GetCurrentDescentLine();
            float sidewaysDistance = sidewaysOffset.magnitude;

            if (sidewaysDistance <= maxSidewaysDeviation)
            {
                // Apply sideways movement
                transform.position += sidewaysDirection;

                if (showRhythmDebug)
                {
                    Debug.Log($"Resistance applied: {(leftMouseDown ? "Left" : "Right")} | Slowdown: {currentResistanceEffect:F2}");
                }
            }
            else if (showRhythmDebug)
            {
                Debug.Log("Maximum sideways deviation reached!");
            }
        }

        // Decay resistance effect over time
        currentResistanceEffect = Mathf.Max(currentResistanceEffect - resistanceDecay * Time.deltaTime, 0f);
    }

    // Get the current point on the straight descent line
    Vector3 GetCurrentDescentLine()
    {
        // Calculate progress along descent path
        float totalDescentDistance = Vector3.Distance(descentStartPosition, initialPosition);
        float currentDescentDistance = Vector3.Distance(transform.position, initialPosition);
        float descentProgress = 1f - (currentDescentDistance / totalDescentDistance);

        // Return point on straight line between descent start and initial position
        return Vector3.Lerp(descentStartPosition, initialPosition, descentProgress);
    }

    // Get the current point on the straight return line (for regular return state)
    Vector3 GetCurrentReturnLine()
    {
        // For return state, the reference line is just the straight path back to initial position
        // Since we're not necessarily coming from max Z, we use current position as start reference
        return Vector3.Lerp(transform.position, initialPosition, 0f); // Just return current position as reference
    }
    bool HasReachedMaxZ()
    {
        Vector3 directionFromInitial = transform.position - initialPosition;
        float forwardDistance = Vector3.Dot(directionFromInitial, transform.forward);
        return forwardDistance >= maxZPosition;
    }

    void PlayStepSound()
    {
        if (audioSource != null && stepSound != null)
        {
            // Generate random pitch within the specified range
            float randomPitch = Random.Range(minPitch, maxPitch);
            audioSource.pitch = randomPitch;

            // Play the step sound
            audioSource.PlayOneShot(stepSound);
        }
    }

    void TakeStep()
    {
        // Calculate new target position one step forward
        Vector3 stepDirection = transform.forward * stepDistance;
        Vector3 potentialTarget = transform.position + stepDirection;

        // Calculate the distance from initial position in the forward direction
        Vector3 directionFromInitial = potentialTarget - initialPosition;
        float forwardDistance = Vector3.Dot(directionFromInitial, transform.forward);

        // Check if we've exceeded the maximum distance
        if (forwardDistance >= maxZPosition)
        {
            // Clamp the target position to the maximum allowed distance
            targetPosition = initialPosition + (transform.forward * maxZPosition);
        }
        else
        {
            targetPosition = potentialTarget;
        }

        isMovingToTarget = true;
        currentState = MovementState.MovingForward;

        // Calculate progressive difficulty for animation speed
        float progressRatio = GetProgressRatio();
        float forwardAnimSpeed = Mathf.Lerp(minForwardSpeed, 1f, difficultycurve.Evaluate(1f - progressRatio));

        // Set animation speed based on difficulty
        if (animator != null)
        {
            animator.SetFloat(speedParameterName, forwardAnimSpeed);
        }
    }

    void MoveTowardsTarget()
    {
        // Calculate progressive difficulty
        float progressRatio = GetProgressRatio();
        float speedMultiplier = Mathf.Lerp(minForwardSpeed, 1f, difficultycurve.Evaluate(1f - progressRatio));

        // Move towards the target position with reduced speed as we get closer to max Z
        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            stepSpeed * speedMultiplier * Time.deltaTime
        );

        // Check if we've reached the target
        if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
        {
            transform.position = targetPosition;
            isMovingToTarget = false;

            // Check if we've reached max Z - start descent
            if (HasReachedMaxZ())
            {
                StartDescent();
            }
            else
            {
                currentState = MovementState.Idle;
                // Set animation speed to 0 when stopped
                if (animator != null)
                {
                    animator.SetFloat(speedParameterName, 0f);
                }
            }
        }
    }

    void StartDescent()
    {
        isDescending = true;
        currentState = MovementState.Descending;
        descentStartPosition = transform.position;

        // Set animation for descent
        if (animator != null)
        {
            animator.SetFloat(speedParameterName, -1f);
        }

        if (showRhythmDebug)
        {
            Debug.Log("Starting descent - resist with left/right clicks!");
        }
    }

    void HandleDescent()
    {
        // Calculate descent speed with resistance effect
        float effectiveDescentSpeed = descentSpeed * (1f - currentResistanceEffect);

        // Automatic movement back towards initial position (slowed by resistance)
        transform.position = Vector3.MoveTowards(
            transform.position,
            initialPosition,
            effectiveDescentSpeed * Time.deltaTime
        );

        // Update animation speed based on descent progress and resistance
        float descentProgress = 1f - (Vector3.Distance(transform.position, initialPosition) /
                                    Vector3.Distance(descentStartPosition, initialPosition));
        float baseAnimSpeed = Mathf.Lerp(-1f, -maxReturnSpeedMultiplier, descentProgress);
        float effectiveAnimSpeed = baseAnimSpeed * (1f - currentResistanceEffect * 0.5f); // Resistance also affects animation

        if (animator != null)
        {
            animator.SetFloat(speedParameterName, effectiveAnimSpeed);
        }

        // Check if we've reached the initial position
        if (Vector3.Distance(transform.position, initialPosition) < 0.1f)
        {
            transform.position = initialPosition;
            isDescending = false;
            currentState = MovementState.Idle;
            currentResistanceEffect = 0f; // Reset resistance
            ResetRhythm(); // Reset for new cycle

            // Set animation speed to 0 when stopped
            if (animator != null)
            {
                animator.SetFloat(speedParameterName, 0f);
            }

            if (showRhythmDebug)
            {
                Debug.Log("Descent complete - ready for new cycle!");
            }
        }
    }

    void StartReturning()
    {
        isReturning = true;
        currentState = MovementState.Returning;

        // Calculate return speed based on distance from initial position
        float returnAnimSpeed = GetReturnAnimationSpeed();

        // Set animation speed for backward movement
        if (animator != null)
        {
            animator.SetFloat(speedParameterName, -returnAnimSpeed);
        }
    }

    void ReturnToInitialPosition()
    {
        // Only move back if we're not already at the initial position
        if (Vector3.Distance(transform.position, initialPosition) > 0.01f)
        {
            // Calculate return speed multiplier based on distance
            float returnSpeedMultiplier = GetReturnSpeedMultiplier();
            float baseReturnSpeed = returnSpeed * returnSpeedMultiplier;

            // Apply resistance effect to return speed
            float effectiveReturnSpeed = baseReturnSpeed * (1f - currentResistanceEffect);

            // Update animation speed continuously during return
            float returnAnimSpeed = GetReturnAnimationSpeed();
            float effectiveAnimSpeed = returnAnimSpeed * (1f - currentResistanceEffect * 0.5f);

            if (animator != null)
            {
                animator.SetFloat(speedParameterName, -effectiveAnimSpeed);
            }

            // Move towards the initial position
            transform.position = Vector3.MoveTowards(
                transform.position,
                initialPosition,
                effectiveReturnSpeed * Time.deltaTime
            );
        }
        else
        {
            // Reset when back at initial position
            transform.position = initialPosition;
            isReturning = false;
            currentState = MovementState.Idle;
            currentResistanceEffect = 0f; // Reset resistance
            ResetRhythm(); // Reset rhythm and alternation pattern

            // Set animation speed to 0 when stopped
            if (animator != null)
            {
                animator.SetFloat(speedParameterName, 0f);
            }
        }
    }

    // Check if the current click fits the established rhythm
    bool IsValidRhythmStep(float currentTime)
    {
        // First click is always valid
        if (lastClickTime == 0f)
        {
            return true;
        }

        float timeSinceLastClick = currentTime - lastClickTime;

        // If we haven't established a rhythm yet, be more lenient
        if (!hasEstablishedRhythm)
        {
            // Accept clicks that are within a reasonable range of the ideal beat
            return timeSinceLastClick >= (idealBeatInterval - rhythmTolerance) &&
                   timeSinceLastClick <= (idealBeatInterval + rhythmTolerance * 2f);
        }

        // If we have established rhythm, check against it
        float deviation = Mathf.Abs(timeSinceLastClick - establishedRhythm);
        bool isInRhythm = deviation <= rhythmTolerance;

        if (showRhythmDebug)
        {
            Debug.Log($"Rhythm check: interval={timeSinceLastClick:F2}, target={establishedRhythm:F2}, deviation={deviation:F2}, valid={isInRhythm}");
        }

        return isInRhythm;
    }

    // Update rhythm tracking based on click timing
    void UpdateRhythmTracking(float currentTime)
    {
        if (lastClickTime > 0f)
        {
            float interval = currentTime - lastClickTime;

            // Add to our interval history
            clickIntervals.Add(interval);

            // Remove old intervals (keep only recent ones)
            while (clickIntervals.Count > minBeatsForRhythm + 2)
            {
                clickIntervals.RemoveAt(0);
            }

            // Try to establish rhythm if we have enough data
            if (clickIntervals.Count >= minBeatsForRhythm)
            {
                UpdateEstablishedRhythm();
            }
        }

        lastClickTime = currentTime;
    }

    // Calculate and update the established rhythm
    void UpdateEstablishedRhythm()
    {
        if (clickIntervals.Count < minBeatsForRhythm) return;

        // Calculate average interval
        float sum = 0f;
        foreach (float interval in clickIntervals)
        {
            sum += interval;
        }
        float average = sum / clickIntervals.Count;

        // Calculate consistency (how much intervals vary from average)
        float variance = 0f;
        foreach (float interval in clickIntervals)
        {
            variance += (interval - average) * (interval - average);
        }
        variance /= clickIntervals.Count;
        float standardDeviation = Mathf.Sqrt(variance);

        // Update rhythm confidence based on consistency
        rhythmConfidence = Mathf.Clamp01(1f - (standardDeviation / rhythmTolerance));

        // Only establish rhythm if confidence is high enough
        if (rhythmConfidence > 0.7f)
        {
            establishedRhythm = average;
            hasEstablishedRhythm = true;

            if (showRhythmDebug)
            {
                Debug.Log($"Established rhythm: {establishedRhythm:F2}s, confidence: {rhythmConfidence:F2}");
            }
        }
    }

    // Reset rhythm tracking
    void ResetRhythm()
    {
        clickIntervals.Clear();
        hasEstablishedRhythm = false;
        rhythmConfidence = 0f;
        establishedRhythm = 0f;
        lastClickTime = 0f;
        lastStepWasLeft = false; // Reset alternation pattern too
    }
    float GetProgressRatio()
    {
        Vector3 directionFromInitial = transform.position - initialPosition;
        float currentDistance = Vector3.Dot(directionFromInitial, transform.forward);
        return Mathf.Clamp01(currentDistance / maxZPosition);
    }

    // Calculate return speed multiplier based on distance from initial position
    float GetReturnSpeedMultiplier()
    {
        float progressRatio = GetProgressRatio();
        return Mathf.Lerp(1f, maxReturnSpeedMultiplier, returnCurve.Evaluate(progressRatio));
    }

    // Calculate return animation speed based on distance from initial position
    float GetReturnAnimationSpeed()
    {
        float progressRatio = GetProgressRatio();
        return Mathf.Lerp(1f, maxReturnSpeedMultiplier, returnCurve.Evaluate(progressRatio));
    }

    // Optional: Method to reset the initial position
    public void SetNewInitialPosition()
    {
        initialPosition = transform.position;
        targetPosition = transform.position;
        isMovingToTarget = false;
        isReturning = false;
        lastStepWasLeft = false;

        // Set animation speed to 0
        if (animator != null)
        {
            animator.SetFloat(speedParameterName, 0f);
        }
    }

    // Optional: Method to change the max Z position at runtime
    public void SetMaxZPosition(float newMaxZ)
    {
        maxZPosition = newMaxZ;
    }

    // Debug visualization in Scene view
    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying)
        {
            // Draw initial position
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(initialPosition, 0.3f);

            // Draw max Z position
            Gizmos.color = Color.red;
            Vector3 maxPos = initialPosition + (transform.forward * maxZPosition);
            Gizmos.DrawWireSphere(maxPos, 0.3f);

            // Draw progress line
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(initialPosition, maxPos);

            // Draw current position and rhythm info
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, 0.2f);

            // Show different states with colors
            if (currentState == MovementState.Descending)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(transform.position + Vector3.up * 0.8f, Vector3.one * 0.2f);

                // Show descent line
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(descentStartPosition, initialPosition);

                // Show sideways deviation limit
                Vector3 descentLinePos = GetCurrentDescentLine();
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(descentLinePos + transform.right * maxSidewaysDeviation, 0.1f);
                Gizmos.DrawWireSphere(descentLinePos - transform.right * maxSidewaysDeviation, 0.1f);
            }
            else if (currentState == MovementState.Returning)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(transform.position + Vector3.up * 0.8f, Vector3.one * 0.15f);
            }
            else if (hasEstablishedRhythm)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, Vector3.one * 0.1f);
            }
        }
    }

    // Get current rhythm info for UI display
    public string GetRhythmInfo()
    {
        string stateInfo = $"State: {currentState}";

        switch (currentState)
        {
            case MovementState.Descending:
                float resistancePercent = currentResistanceEffect * 100f;
                return $"{stateInfo} | Resist: {resistancePercent:F0}% | LEFT/RIGHT to slow descent!";

            case MovementState.Returning:
                return $"{stateInfo} | Automatic return...";

            default:
                string nextButton = lastStepWasLeft ? "RIGHT" : "LEFT";

                if (hasEstablishedRhythm)
                {
                    return $"{stateInfo} | Rhythm: {establishedRhythm:F2}s | Next: {nextButton}";
                }
                else
                {
                    return $"{stateInfo} | Learning rhythm... ({clickIntervals.Count}/{minBeatsForRhythm}) | Next: {nextButton}";
                }
        }
    }
}