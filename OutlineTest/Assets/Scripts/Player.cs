using UnityEngine;
using System.Collections;

public class Player : MonoBehaviour
{
    Animator animator;
    float speed;
    Vector3 up;

	// Use this for initialization
	void Start ()
    {
        animator = GetComponent<Animator>();
        speed = 0.0f;
        up.Set(0, 1, 0);
	}
	
	// Update is called once per frame
	void Update ()
    {
        const float maxSpeed = 1.0f;
        const float speedGain = 10.0f;
        const float maxAngularSpeed = 180.0f;
        const float angularGain = 30.0f;
        const float magnifier = 1.5f;

        // Get the desired direction and speed from the control stick
        float leftX = Input.GetAxis("LeftX") * magnifier;
        float leftY = Input.GetAxis("LeftY") * magnifier;
        Vector3 desiredDirectionCameraSpace = new Vector3(leftX, 0.0f, leftY);
        float length = desiredDirectionCameraSpace.magnitude;
        if (length > 1.0f)
        {
            desiredDirectionCameraSpace.Normalize();
        }
        Vector3 desiredDirection = Camera.main.transform.rotation * desiredDirectionCameraSpace;
        desiredDirection -= up * Vector3.Dot(up, desiredDirection);
        if (desiredDirection.magnitude > 0.0f)
        {
            // Rotate towards the desired direction
            desiredDirection.Normalize();
            Vector3 forward = new Vector3(0, 0, 1);
            Vector3 currentDirection = transform.rotation * forward;
            Vector3 currentRight = new Vector3(currentDirection.z, 0.0f, -currentDirection.x);
            float sign = Mathf.Sign(Vector3.Dot(desiredDirection, currentRight));
            float dot = Vector3.Dot(desiredDirection, currentDirection);
            float angle = Mathf.Acos(Mathf.Clamp01(dot)) * Mathf.Rad2Deg;
            if (float.IsNaN(angle))
            {
                UnityEngine.Debug.Log(desiredDirection + " " + currentDirection + " -> " + dot);
            }
            float g = angularGain * Time.deltaTime;
            float m = maxAngularSpeed * Time.deltaTime;
            angle = Mathf.Min(m, angle * Mathf.Min(g, 1.0f));
            transform.rotation = Quaternion.AngleAxis(angle * sign, up) * transform.rotation;
        }

        // Accelerate towards the desired speed
        float desiredSpeed = Mathf.Min(desiredDirectionCameraSpace.magnitude, 1.0f) * maxSpeed;
        const float maxAcceleration = 0.025f;
        float acceleration = (desiredSpeed - speed) * speedGain * Time.deltaTime;
        acceleration = Mathf.Min(maxAcceleration, acceleration);
        acceleration = Mathf.Max(-maxAcceleration, acceleration);
        speed += acceleration;
        animator.SetFloat("Speed", speed);
    }
}
