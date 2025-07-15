using UnityEngine;

public class MouseWalkController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float stepDistance = 1f;
    public float stepSpeed = 5f;
    public float returnSpeed = 3f;
    public float maxZPosition = 10f; // Maximum Z position limit

    [Header("Animation")]
    public Animator animator;
    public string speedParameterName = "Speed";

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip stepSound;
    public float minPitch = 0.8f;
    public float maxPitch = 1.2f;

    private Vector3 initialPosition;
    private Vector3 targetPosition;
    private bool isMovingToTarget = false;
    private bool isReturning = false;

    private bool leftMousePressed = false;
    private bool rightMousePressed = false;
    private bool lastStepWasLeft = false;

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

        // Handle stepping logic
        if (leftMouseDown || rightMouseDown)
        {
            // Play step sound regardless of whether step is valid
            PlayStepSound();

            // Check if this is a valid step (alternating pattern)
            bool validStep = false;

            if (leftMouseDown && !lastStepWasLeft)
            {
                validStep = true;
                lastStepWasLeft = true;
            }
            else if (rightMouseDown && lastStepWasLeft)
            {
                validStep = true;
                lastStepWasLeft = false;
            }

            if (validStep)
            {
                TakeStep();
                isReturning = false;
            }
        }

        // Handle movement
        if (isMovingToTarget)
        {
            MoveTowardsTarget();
        }
        else if (!isReturning)
        {
            // Start returning to initial position immediately
            StartReturning();
        }

        if (isReturning)
        {
            ReturnToInitialPosition();
        }
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

        // Check Z position limit (assuming forward is positive Z)
        float localZPosition = transform.InverseTransformPoint(potentialTarget).z;
        float initialLocalZ = transform.InverseTransformPoint(initialPosition).z;

        // Limit the target position based on max Z distance from initial position
        if (localZPosition - initialLocalZ >= maxZPosition)
        {
            // Calculate the maximum allowed position
            Vector3 maxForwardDirection = transform.forward * maxZPosition;
            targetPosition = initialPosition + maxForwardDirection;
        }
        else
        {
            targetPosition = potentialTarget;
        }

        isMovingToTarget = true;

        // Set animation speed to 1 (forward)
        if (animator != null)
        {
            animator.SetFloat(speedParameterName, 1f);
        }
    }

    void MoveTowardsTarget()
    {
        // Move towards the target position
        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            stepSpeed * Time.deltaTime
        );

        // Check if we've reached the target
        if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
        {
            transform.position = targetPosition;
            isMovingToTarget = false;

            // Set animation speed to 0 when stopped
            if (animator != null)
            {
                animator.SetFloat(speedParameterName, 0f);
            }
        }
    }

    void StartReturning()
    {
        isReturning = true;

        // Set animation speed to -1 (backward)
        if (animator != null)
        {
            animator.SetFloat(speedParameterName, -1f);
        }
    }

    void ReturnToInitialPosition()
    {
        // Only move back if we're not already at the initial position
        if (Vector3.Distance(transform.position, initialPosition) > 0.01f)
        {
            // Move towards the initial position
            transform.position = Vector3.MoveTowards(
                transform.position,
                initialPosition,
                returnSpeed * Time.deltaTime
            );
        }
        else
        {
            // Reset when back at initial position
            transform.position = initialPosition;
            isReturning = false;
            lastStepWasLeft = false; // Reset stepping pattern

            // Set animation speed to 0 when stopped
            if (animator != null)
            {
                animator.SetFloat(speedParameterName, 0f);
            }
        }
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
}