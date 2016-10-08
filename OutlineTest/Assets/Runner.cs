using UnityEngine;
using System.Collections;

public class Runner : MonoBehaviour {

    Animator animator;
    float speed;
    float angularSpeed;
    Vector3 up;
    float acceleration;
    bool falling;

    Vector3 lastPosition;
    Vector3 velocity;

    Vector3 targetCameraOffsetLs;
    Quaternion targetCameraRotationLs;
    Vector3 targetCameraPositionWs;

    bool idleMode; // debug

    void Start ()
    {

        animator = GetComponent<Animator>();
        speed = 1.0f;
        angularSpeed = 0.0f;
        up.Set(0, 1, 0);
        acceleration = 0.02f;
        falling = false;

        idleMode = false;

        targetCameraOffsetLs = new Vector3(0.0f, 3.0f, -6.0f);
        targetCameraRotationLs = Quaternion.AngleAxis(30.0f, new Vector3(1, 0, 0));
    }
	
	void Update ()
    {
        if (idleMode)
        {
            animator.SetTrigger("Fall");
            return;
        }

        // Check ground
        if (!falling)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position + up, -2 * up, out hit))
            {

            }
            else
            {
                animator.SetTrigger("Fall");
                falling = true;
                velocity = (lastPosition - transform.position) * (1.0f / Time.deltaTime);
            }
        }

        Quaternion targetCameraRotationWs;
        if (falling)
        {
            const float gravity = 9.8f;
            const float airGain = 0.5f;

            float airGainDt = Mathf.Min(airGain * Time.deltaTime, 1.0f);
            velocity *= (1.0f - airGainDt);
            velocity -= up * gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;

            // Rotate the camera to look at the falling player without moving it
            Vector3 lookDifference = transform.position - targetCameraPositionWs;
            Vector3 lookDirection = lookDifference;
            lookDirection.Normalize();
            Vector3 upDirection = Vector3.Cross(Vector3.Cross(up, lookDirection), up);
            upDirection.Normalize();
            targetCameraRotationWs = Quaternion.LookRotation(lookDirection, upDirection);
        }
        else
        {
            const float magnifier = 1.5f;
            const float maxAngularSpeed = 180.0f;
            const float angularSpeedGain = 10.0f;

            // Rotate the character
            float angularSpeedGainDt = Mathf.Min(1.0f, angularSpeedGain * Time.deltaTime);
            float leftX = Mathf.Clamp(Input.GetAxis("LeftX") * magnifier, -1.0f, 1.0f);
            float desiredAngularSpeed = leftX * maxAngularSpeed;
            angularSpeed += (desiredAngularSpeed - angularSpeed) * angularSpeedGainDt; // TODO timestep independent gain?

            float deltaAngle = angularSpeed * Time.deltaTime;
            transform.rotation = Quaternion.AngleAxis(deltaAngle, up) * transform.rotation;

            // Control the character's speed
            speed += acceleration * Time.deltaTime;
            animator.SetFloat("RunSpeed", speed);

            // Have the camera follow the player
            float cameraOffsetScale = 1.0f + (speed - 1.0f) * 0.5f;
            targetCameraPositionWs = transform.TransformPoint(targetCameraOffsetLs * cameraOffsetScale);
            targetCameraRotationWs = transform.rotation;

            lastPosition = transform.position;
        }

        // Update the camera
        const float cameraGain = 10.0f;
        float cameraGainDt = Mathf.Min(1.0f, cameraGain * Time.deltaTime);
        Camera camera = Camera.main;
        camera.transform.position += (targetCameraPositionWs - camera.transform.position) * cameraGainDt;
        camera.transform.rotation = Quaternion.Slerp(camera.transform.rotation, targetCameraRotationWs, cameraGainDt);
    }
}
