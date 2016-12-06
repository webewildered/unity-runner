using UnityEngine;
using System.Collections;

public class Runner : MonoBehaviour
{
    enum State
    {
        Running,
        Jumping,
        Gliding,
        Falling,
        Dying,
        Dead,
        Resetting
    };

    public Camera DeathCamera;
    public FloorGenerator Floor;

    public GameObject OrbsText;
    public GameObject DistanceText;

    public bool DebugInfinitePower = false;

    Animator animator;
    float speed;
    float currentSpeed; // speed with power modifier
    float angularSpeed;
    Vector3 up;
    const float acceleration = 0.015f;

    float distance; // plane-confirmed distance
    float currentDistance; // plus distance since the last plane
    public float BestDistance;

    // state machine
    State state;

    Vector3 lastPosition;
    Vector3 velocity;

    Vector3 targetCameraOffsetLs;
    Vector3 targetCameraPositionWs;

    float angleUp;      // Rotation about the world space up axis
    float angleForward; // Rotation about the local space forward axis
    float angleRight;   // Rotation about the local space right axis

    int power;

    const float baseSpeed = 15.0f;
    const float baseSpeedFactor = 1.2f;

    // Movement speed for the reset animation
    const float resetFallSpeed = 10.0f;

    public bool IdleMode = false; // debug

    // Layer mask for collision with everything other than the character and debris
    const int layerMask = ~((1 << 8) | (1 << 9));

    // Gain used for camera position
    const float cameraGain = 10.0f;

    // Maximum distance from the origin, if exceeded the world is recentered
    const float maxPosition = 100.0f;   // just for testing, should be higher

    void Start()
    {
        animator = GetComponent<Animator>();
        speed = baseSpeedFactor;
        currentSpeed = 1.0f;
        angularSpeed = 0.0f;
        up.Set(0, 1, 0);
        state = State.Running;
        power = 0;
        distance = 0.0f;
        currentDistance = 0.0f;
        BestDistance = 0.0f; // TODO load from wherever

        angleUp = 0.0f;
        angleForward = 0.0f;
        angleRight = 0.0f;

        const float pitch = 25.0f;
        angleRight = pitch;

        targetCameraOffsetLs = new Vector3(0.0f, 6.0f, -4.0f);
    }

    void OnTriggerEnter(Collider other)
    {
        power++;
    }

    bool checkGround(out RaycastHit hit)
    {
        return Physics.Raycast(transform.position + up, -2 * up, out hit, Mathf.Infinity, layerMask);
    }

    void fall()
    {
        // Transition to fall
        animator.SetTrigger("Fall");
        state = State.Falling;
        angularSpeed = 0.0f;
        Floor.OnPlayerDied();
        gameObject.GetComponent<CapsuleCollider>().enabled = false;
        setBestDistance();
    }

    void setBestDistance()
    {
        BestDistance = Mathf.Max(BestDistance, currentDistance);
    }

    public void Reset()
    {
        state = State.Resetting;

        // Reset all properties
        speed = baseSpeedFactor;
        distance = 0.0f;
        currentDistance = 0.0f;
        power = 0;

        // Enable components that might have been disabled for dead state
        gameObject.GetComponentInChildren<SkinnedMeshRenderer>().enabled = true;
        gameObject.GetComponent<CapsuleCollider>().enabled = true;

        // Position the character to glide in so that it lands at the correct position in front of the camera
        Vector3 right = Vector3.Cross(Util.Up, Camera.main.transform.forward);
        Vector3 forward = Vector3.Cross(right, Util.Up);
        transform.rotation = Quaternion.FromToRotation(Util.Forward, forward);
        Vector3 targetPosition = Camera.main.transform.position - transform.rotation * targetCameraOffsetLs;
        const float resetDistance = 10.0f;
        Vector3 resetDirection = forward * baseSpeed - Util.Up * resetFallSpeed;
        transform.position = targetPosition - (resetDistance / resetDirection.magnitude) * resetDirection;

        // Instant switch to gliding animation
        animator.SetTrigger("GlideInstant");

        // Possible that the character fell while jumping, so the fall trigger never gets consumed and doesn't get automatically cleared by unity
        // (seems like odd behavior to me).
        animator.ResetTrigger("Fall");
    }

    void Update()
    {
        const float jumpingGravity = 30.0f;

        // Debug only
        if (IdleMode)
        {
            animator.SetTrigger("Fall");
            return;
        }

        lastPosition = transform.position;

        //
        // Stage 1: state transitions
        //

        RaycastHit hit;
        switch (state)
        {
            case State.Running:
            {
                // Check ground
                if (checkGround(out hit))
                {
                    // Check jump
                    const int jumpCost = 2;
                    if ((Input.GetKeyDown("joystick button 0") || Input.GetKeyDown("z")) &&
                        animator.GetCurrentAnimatorStateInfo(0).IsName("Run") &&
                        (power >= jumpCost || DebugInfinitePower))
                    {
                        // Transition to jump
                        const float jumpSpeed = 40.0f;
                        float normalizedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                        animator.SetBool("Left", normalizedTime < 0.25f || normalizedTime > 0.75f);
                        animator.SetTrigger("Jump");
                        state = State.Jumping;
                        velocity += (jumpSpeed - Vector3.Dot(velocity, up)) * up;
                        angularSpeed = 0.0f;
                        power -= jumpCost;
                    }

                    // Check bomb
                    const int bombCost = 3;
                    if ((Input.GetKeyDown("joystick button 1") || Input.GetKeyDown("x")) &&
                        (power >= bombCost || DebugInfinitePower))
                    {
                        const float bombRadius = 30.0f;
                        Collider[] bombHits = Physics.OverlapSphere(transform.position, bombRadius);
                        foreach (Collider bombHit in bombHits)
                        {
                            Obstacle obstacle = bombHit.gameObject.GetComponent<Obstacle>();
                            if (obstacle != null)
                            {
                                obstacle.Bomb(transform.position, bombRadius);
                            }
                        }
                        power -= bombCost;
                    }
                }
                else
                {
                    fall();
                }
                break;
            }

            case State.Jumping:
            {
                const float glideSpeed = 15.0f;
                float upSpeed = Vector3.Dot(velocity, up);
                if (upSpeed < glideSpeed)
                {
                    // Transition to gliding
                    state = State.Gliding;
                    animator.SetTrigger("Glide");
                }
                break;
            }

            case State.Gliding:
            case State.Resetting:
            {
                if (transform.position.y + velocity.y * Time.deltaTime < 0.0f)
                {
                    if (checkGround(out hit))
                    {
                        // Transition to landing
                        state = State.Running;
                        animator.SetTrigger("Land");

                        Vector3 position = transform.position;
                        position.y = 0.0f;
                        transform.position = position;
                    }
                    else
                    {
                        // Missed the ground, fall
                        fall();
                    }
                }
                break;
            }

            case State.Dying:
            {
                state = State.Dead;
                Floor.OnPlayerDied();
                angularSpeed = 0.0f;
                gameObject.GetComponentInChildren<SkinnedMeshRenderer>().enabled = false;
                gameObject.GetComponent<CapsuleCollider>().enabled = false;
                setBestDistance();
                break;
            }

            // No transitions from State.Falling
            default: break;
        }

        // Get input from gamepad, override with keyboard
        const float magnifier = 1.5f;
        float leftX = Mathf.Clamp(Input.GetAxis("LeftX") * magnifier, -1.0f, 1.0f);
        if (Input.GetKey("left"))
        {
            if (Input.GetKey("right"))
            {
                leftX = 0.0f;
            }
            else
            {
                leftX = -1.0f;
            }
        }
        else if (Input.GetKey("right"))
        {
            leftX = 1.0f;
        }

        //
        // Stage 2: state updates
        //

        Quaternion targetCameraRotationWs = Quaternion.identity;
        Vector3 characterForward;
        switch (state)
        {
            case State.Running:
                const float maxAngularSpeed = 60.0f;
                const float angularSpeedGain = 7.0f;
                const float maxAngularAcceleration = 135.0f;

                // Rotate the character
                float desiredAngularSpeed = leftX * maxAngularSpeed * currentSpeed;
                float angularSpeedChange = (desiredAngularSpeed - angularSpeed) * Util.Gain(angularSpeedGain);
                float angularSpeedChange2 = Mathf.Sign(angularSpeedChange) * Mathf.Min(maxAngularAcceleration * currentSpeed * Time.deltaTime, Mathf.Abs(angularSpeedChange));
                angularSpeed += angularSpeedChange2; // TODO timestep independent gain?

                float deltaAngle = angularSpeed * Time.deltaTime;
                angleUp += deltaAngle;

                // Move the character forward
                characterForward = Quaternion.AngleAxis(angleUp, Util.Up) * Util.Forward;
                float forwardSpeed = baseSpeed * currentSpeed;
                transform.position += characterForward * (forwardSpeed * Time.deltaTime);

                // Calculate velocity, used for state change next step
                velocity = (transform.position - lastPosition) * (1.0f / Time.deltaTime);
                break;

            case State.Jumping:
                velocity -= up * jumpingGravity * Time.deltaTime;
                transform.position += velocity * Time.deltaTime;
                break;

            case State.Gliding:
                // Adjust vertical speed, use gravity but low terminal velocity
                const float baseFallSpeed = 10.0f;
                float fallSpeed = baseFallSpeed * currentSpeed;
                float currentDownSpeed = -Vector3.Dot(velocity, up);
                float downSpeed = currentDownSpeed + jumpingGravity * Time.deltaTime;
                downSpeed = Mathf.Min(downSpeed, fallSpeed);
                velocity += (currentDownSpeed - downSpeed) * up;
                velocity -= up * jumpingGravity * Time.deltaTime;
                transform.position += velocity * Time.deltaTime;

                // Player can influence horizontal speed, but not turn
                const float maxHorizontalSpeed = 20.0f;
                const float horizontalSpeedGain = 20.0f;
                const float maxHorizontalAcceleration = 20.0f;

                characterForward = transform.rotation * new Vector3(0, 0, 1);
                Vector3 right = Vector3.Cross(up, characterForward);
                float horizontalSpeed = Vector3.Dot(velocity, right);

                float desiredHorizontalSpeed = leftX * maxHorizontalSpeed * currentSpeed;
                float horizontalSpeedChange = (desiredHorizontalSpeed - horizontalSpeed) * Util.Gain(horizontalSpeedGain);
                float horizontalSpeedChange2 = Mathf.Sign(horizontalSpeedChange) * Mathf.Min(maxHorizontalAcceleration * currentSpeed * Time.deltaTime, Mathf.Abs(horizontalSpeedChange));
                velocity += horizontalSpeedChange2 * right;

                angleForward = -horizontalSpeed;

                break;

            case State.Falling:
                const float fallingGravity = 30.0f;
                const float airGain = 1.0f;

                velocity *= (1.0f - Util.Gain(airGain));
                velocity -= up * fallingGravity * Time.deltaTime;
                transform.position += velocity * Time.deltaTime;
                /*
                // Rotate the camera to look at the falling player without moving it
                Vector3 lookDifference = transform.position - targetCameraPositionWs;
                Vector3 lookDirection = lookDifference;
                lookDirection.Normalize();
                Vector3 upDirection = Vector3.Cross(Vector3.Cross(up, lookDirection), up);
                upDirection.Normalize();
                targetCameraRotationWs = Quaternion.LookRotation(lookDirection, upDirection);
                */
                break;

            case State.Resetting:
                Vector3 forward = transform.rotation * Util.Forward;
                velocity = baseSpeed * forward - resetFallSpeed * Util.Up;
                transform.position += velocity * Time.deltaTime;
                break;

            default:
                break;
        }

        // Neutralize forward rotation while not gliding
        if (state != State.Gliding)
        {
            const float rotationForwardGain = 10.0f;
            angleForward -= angleForward * Util.Gain(rotationForwardGain);
        }

        // Set the character rotation
        Quaternion rotationUp = Quaternion.AngleAxis(angleUp, Util.Up);
        Vector3 forwardAxis = rotationUp * Util.Forward;
        transform.rotation = Quaternion.AngleAxis(angleForward, forwardAxis) * rotationUp;

        // Have the camera follow the player
        //if (state != State.Falling)
        {
            if (state != State.Resetting && state != State.Falling)
            {
                targetCameraPositionWs = transform.TransformPoint(targetCameraOffsetLs);
            }
            targetCameraRotationWs = transform.rotation;

            Vector3 forward = targetCameraRotationWs * new Vector3(0, 0, 1);
            Quaternion tilt = Quaternion.AngleAxis(-angularSpeed * 0.1f, forward);
            targetCameraRotationWs = tilt * targetCameraRotationWs;
        }

        // Pitch the camera
        {
            Vector3 r = targetCameraRotationWs * new Vector3(1, 0, 0);
            Quaternion pitch = Quaternion.AngleAxis(angleRight, r);
            targetCameraRotationWs = pitch * targetCameraRotationWs;
        }

        // Update the camera
        const float cameraAngularGain = 4.0f;
        Camera camera = Camera.main;
        camera.transform.position += (targetCameraPositionWs - camera.transform.position) * Util.Gain(cameraGain);
        camera.transform.rotation = Quaternion.Slerp(camera.transform.rotation, targetCameraRotationWs, Util.Gain(cameraAngularGain));

        // Check for collision
        if (state != State.Falling)
        {
            Ray sphereRay = new Ray();
            sphereRay.origin = lastPosition + 0.9f * up;
            Vector3 dir = transform.position - lastPosition;
            sphereRay.direction = dir;
            sphereRay.direction.Normalize();

            RaycastHit hitInfo;
            if (Physics.SphereCast(sphereRay, 0.4f, out hitInfo, dir.magnitude, layerMask))
            {
                Debug.Log("Hit " + hitInfo.collider.gameObject.name + "(" + hitInfo.collider.gameObject.layer + ")");

                // Transition to dead
                state = State.Dying;
                DeathCamera.GetComponent<DeathSnapshot>().Snap(velocity);
            }
        }

        // Alive updates
        if (state != State.Falling && state != State.Dying && state != State.Dead && state != State.Resetting)
        {
            // Check for plane crossings
            Vector3 positionLocal = Floor.transform.InverseTransformPoint(transform.position);
            while (FloorGenerator.Instance.Planes.Count > 0 && planeTest(positionLocal, FloorGenerator.Instance.Planes.Peek()))
            {
                Floor.Planes.Dequeue();
                distance += FloorGenerator.Instance.Res;
            }
            currentDistance = distance;
            if (FloorGenerator.Instance.Planes.Count > 0)
            {
                Vector4 plane = FloorGenerator.Instance.Planes.Peek();
                currentDistance += FloorGenerator.Instance.Res + Vector3.Dot(positionLocal, new Vector3(plane.x, plane.y, plane.z)) - plane.w;
            }

            if (FloorGenerator.Instance.TriggerPlanes.Count > 0 && planeTest(positionLocal, FloorGenerator.Instance.TriggerPlanes.Peek()))
            {
                UnityEngine.Debug.Log("trigger " + FloorGenerator.Instance.Planes.Peek());
                Floor.TriggerPlanes.Dequeue();
                Floor.Advance();
            }

            // Accelerate
            speed += acceleration * Time.deltaTime;

            const float powerSpeed = 0.02f;
            currentSpeed = speed + power * powerSpeed;

            const float animSpeedConversion = 0.8f;
            animator.SetFloat("RunSpeed", rate(animSpeedConversion));

            // World shift
            if (transform.position.magnitude > maxPosition)
            {
                Vector3 posXZ = transform.position;
                posXZ.y = 0.0f;
                FloorGenerator.Instance.transform.position -= posXZ;
                Camera.main.transform.position -= posXZ;
                transform.position -= posXZ;
            }
        }

        // Update texts
        OrbsText.GetComponent<UnityEngine.UI.Text>().text = power.ToString();
        const float distanceFactor = 0.3f;
        float displayDistance = Mathf.Max(0.0f, (currentDistance) * distanceFactor);
        DistanceText.GetComponent<UnityEngine.UI.Text>().text = displayDistance.ToString("N1") + "m";
    }

    float rate(float factor)
    {
        return 1.0f + (currentSpeed - 1.0f) * factor;
    }

    bool planeTest(Vector3 position, Vector4 plane)
    {
        return (Vector3.Dot(position, new Vector3(plane.x, plane.y, plane.z)) - plane.w > 0);
    }
}
