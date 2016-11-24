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
        Dead
    };

    public Camera DeathCamera;
    public FloorGenerator Floor;

    Animator animator;
    float speed;
    float angularSpeed;
    Vector3 up;
    float acceleration;

    // state machine
    State state;

    Vector3 lastPosition;
    Vector3 velocity;

    Vector3 targetCameraOffsetLs;
    Vector3 targetCameraPositionWs;
   
    Quaternion rotationUp;      // Rotation about the up axis
    Quaternion rotationForward; // Rotation about the forward axis, after rotationUp

    public bool IdleMode = false; // debug

    // Layer mask for collision with everything other than the character and debris
    const int layerMask = ~((1 << 8) | (1 << 9));

    void Start ()
    {
        animator = GetComponent<Animator>();
        speed = 1.0f;
        angularSpeed = 0.0f;
        up.Set(0, 1, 0);
        acceleration = 0.05f;
        state = State.Running;

        rotationUp = Quaternion.identity;
        rotationForward = Quaternion.identity;

        targetCameraOffsetLs = new Vector3(0.0f, 3.0f, -6.0f);
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

        if (state == State.Dead)
        {
            Floor.OnPlayerDied();
            GameObject.Destroy(gameObject);
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
            // Check ground
            if (checkGround(out hit))
            {
                // Check jump
                if (Input.GetKeyDown("joystick button 0") &&
                    animator.GetCurrentAnimatorStateInfo(0).IsName("Run"))
                {
                    // Transition to jump
                    const float jumpSpeed = 40.0f;
                    float normalizedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    animator.SetBool("Left", normalizedTime < 0.25f || normalizedTime > 0.75f);
                    animator.SetTrigger("Jump");
                    state = State.Jumping;
                    velocity += (jumpSpeed - Vector3.Dot(velocity, up)) * up;
                }

                // Check bomb
                if (Input.GetKeyDown("joystick button 1"))
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
                }
            }
            else
            {
                fall();
            }
            break;

            case State.Jumping:
            const float glideSpeed = 15.0f;
            float upSpeed = Vector3.Dot(velocity, up);
            if (upSpeed < glideSpeed)
            {
                // Transition to gliding
                state = State.Gliding;
                animator.SetTrigger("Glide");
            }
            break;

            case State.Gliding:
            if (transform.position.y + velocity.y * Time.deltaTime < 0.0f)
            {
                if (checkGround(out hit))
                {
                    // Transition to landing
                    state = State.Running;
                    animator.SetTrigger("Land");
                }
                else
                {
                    // Missed the ground, fall
                    fall();
                }
            }
            break;

            // No transitions from State.Falling

            default:
            break;
        }

        //
        // Stage 2: state updates
        //

        Quaternion targetCameraRotationWs = Quaternion.identity;
        Vector3 characterForward;
        const float magnifier = 1.5f;
        float leftX = Mathf.Clamp(Input.GetAxis("LeftX") * magnifier, -1.0f, 1.0f);
        switch (state)
        {
            case State.Running:
            const float baseSpeed = 15.0f;
            const float maxAngularSpeed = 65.0f;
            const float angularSpeedGain = 10.0f;
            const float maxAngularAcceleration = 180.0f;

            // Rotate the character
            float desiredAngularSpeed = leftX * maxAngularSpeed * speed;
            float angularSpeedChange = (desiredAngularSpeed - angularSpeed) * Util.Gain(angularSpeedGain);
            float angularSpeedChange2 = Mathf.Sign(angularSpeedChange) * Mathf.Min(maxAngularAcceleration * speed * Time.deltaTime, Mathf.Abs(angularSpeedChange));
            angularSpeed += angularSpeedChange2; // TODO timestep independent gain?

            float deltaAngle = angularSpeed * Time.deltaTime;
            rotationUp = Quaternion.AngleAxis(deltaAngle, up) * rotationUp;

            // Move the character forward
            characterForward = rotationUp * new Vector3(0, 0, 1);
            float forwardSpeed = baseSpeed * speed;
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
            float fallSpeed = baseFallSpeed * speed;
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

            float desiredHorizontalSpeed = leftX * maxHorizontalSpeed * speed;
            float horizontalSpeedChange = (desiredHorizontalSpeed - horizontalSpeed) * Util.Gain(horizontalSpeedGain);
            float horizontalSpeedChange2 = Mathf.Sign(horizontalSpeedChange) * Mathf.Min(maxHorizontalAcceleration * speed * Time.deltaTime, Mathf.Abs(horizontalSpeedChange));
            velocity += horizontalSpeedChange2 * right;

            rotationForward = Quaternion.AngleAxis(-horizontalSpeed, characterForward);

            break;

            case State.Falling:
            const float fallingGravity = 30.0f;
            const float airGain = 1.0f;

            velocity *= (1.0f - Util.Gain(airGain));
            velocity -= up * fallingGravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;

            // Rotate the camera to look at the falling player without moving it
            Vector3 lookDifference = transform.position - targetCameraPositionWs;
            Vector3 lookDirection = lookDifference;
            lookDirection.Normalize();
            Vector3 upDirection = Vector3.Cross(Vector3.Cross(up, lookDirection), up);
            upDirection.Normalize();
            targetCameraRotationWs = Quaternion.LookRotation(lookDirection, upDirection);
            break;

            // No transitions from State.Falling

            default:
            break;
        }

        // Neutralize forward rotation while not gliding
        if (state != State.Gliding)
        {
            const float rotationForwardGain = 10.0f;
            rotationForward = Quaternion.Slerp(rotationForward, Quaternion.identity, Util.Gain(rotationForwardGain));
        }

        // Set the character rotation
        transform.rotation = rotationUp * rotationForward;

        // Have the camera follow the player
        if (state != State.Falling)
        {
            float cameraOffsetScale = 1.0f;// + (speed - 1.0f) * 0.5f;
            targetCameraPositionWs = transform.TransformPoint(targetCameraOffsetLs * cameraOffsetScale);
            targetCameraRotationWs = transform.rotation;

            Vector3 forward = targetCameraRotationWs * new Vector3(0, 0, 1);
            Quaternion tilt = Quaternion.AngleAxis(-angularSpeed * 0.1f, forward);
            targetCameraRotationWs = tilt * targetCameraRotationWs;
        }

        // Update the camera
        const float cameraGain = 10.0f;
        const float cameraAngularGain = 4.0f;
        Camera camera = Camera.main;
        camera.transform.position += (targetCameraPositionWs - camera.transform.position) * Util.Gain(cameraGain);
        camera.transform.rotation = Quaternion.Slerp(camera.transform.rotation, targetCameraRotationWs, Util.Gain(cameraAngularGain));

        // Check for collision
        Ray sphereRay = new Ray();
        sphereRay.origin = lastPosition + 0.9f * up;
        Vector3 dir = transform.position - lastPosition;
        sphereRay.direction = dir;
        sphereRay.direction.Normalize();
        if (Physics.SphereCast(sphereRay, 0.4f, dir.magnitude, layerMask))
        {
            // Transition to dead
            state = State.Dead;
            DeathCamera.GetComponent<DeathSnapshot>().Snap(velocity);
        }

        // Check for plane crossing
        Vector4 triggerPlane = Floor.TriggerPlanes.Peek();
        Vector3 trigger = new Vector3(triggerPlane.x, triggerPlane.y, triggerPlane.z);
        if (Vector3.Dot(transform.position, trigger) - triggerPlane.w > 0)
        {
            Floor.TriggerPlanes.Dequeue();
            Floor.Advance();
        }

        // Accelerate
        speed += acceleration * Time.deltaTime;
        animator.SetFloat("RunSpeed", speed);
    }
}
