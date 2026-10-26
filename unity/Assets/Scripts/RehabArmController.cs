
using UnityEngine;

public class RehabArmController : MonoBehaviour
{
    public event System.Action<string, int, int, string> OnFeedbackChanged;

    public int Repetitions => repetitions;
    public int GoalRepetitions => goalRepetitions;
    public bool IsCompleted => completed;

    private void NotifyFeedback(string command, string status)
    {
        OnFeedbackChanged?.Invoke(
            command,
            repetitions,
            goalRepetitions,
            status
        );
    }
    [Header("Right Arm")]
    public Transform arm;
    public Transform hand;
    public Transform target;
    public Renderer feedbackRenderer;

    [Header("Left Arm")]
    public Transform leftArm;
    public Transform leftHand;
    public Transform leftTarget;
    public Renderer leftFeedbackRenderer;

    [Header("Movement")]
    public float moveSpeed = 60f;
    public float maxAngle = 100f;
    public float targetDistance = 0.2f;

    [Header("Rotation")]
    public Vector3 rightAxis = Vector3.forward;
    public Vector3 leftAxis = Vector3.forward;
    public float rightDirection = 1f;
    public float leftDirection = -1f;

    [Header("Colors")]
    public Color waitingColor = Color.yellow;
    public Color movingColor = Color.cyan;
    public Color successColor = Color.green;
    public Color missColor = Color.red;

    [Header("Exercise")]
    public int goalRepetitions = 5;

    private Quaternion rightStartRotation;
    private Quaternion leftStartRotation;

    private Material rightMaterial;
    private Material leftMaterial;

    private Transform activeArm;
    private Transform activeHand;
    private Transform activeTarget;
    private Quaternion activeStartRotation;
    private Material activeMaterial;
    private Vector3 activeAxis;
    private float activeDirection;

    private float angle;
    private float targetAngle;
    private int repetitions;

    private bool moving;
    private bool returning;
    private bool completed;
    private bool targetReached;

    private string activeCommand = "";

    void Start()
    {
        if (arm == null || hand == null || target == null ||
            leftArm == null || leftHand == null || leftTarget == null)
        {
            Debug.LogError(
                "Назначь обе руки, обе кисти и обе мишени в Inspector."
            );
            enabled = false;
            return;
        }

        rightStartRotation = arm.localRotation;
        leftStartRotation = leftArm.localRotation;

        if (feedbackRenderer != null)
            rightMaterial = feedbackRenderer.material;

        if (leftFeedbackRenderer != null)
            leftMaterial = leftFeedbackRenderer.material;

        SetColor(rightMaterial, waitingColor);
        SetColor(leftMaterial, waitingColor);

        Debug.Log("Тренажёр готов. Ожидание команды ЭЭГ.");
        NotifyFeedback("", "ready");
    }

    void Update()
    {
        if (completed)
        {
            if (Input.GetKeyDown(KeyCode.Return))
                ResetExercise();

            return;
        }

        if (!moving && !returning)
        {
            if (Input.GetKeyDown(KeyCode.E))
                SetEEGCommand("right_hand");

            if (Input.GetKeyDown(KeyCode.Q))
                SetEEGCommand("left_hand");

            if (Input.GetKeyDown(KeyCode.R))
                BeginReturn(false);
        }

        if (moving)
            MoveToTarget();

        if (returning)
            ReturnArm();
    }

    public void SetEEGCommand(string predictedClass)
    {
        if (completed || moving || returning)
            return;

        if (predictedClass == "right_hand")
        {
            activeArm = arm;
            activeHand = hand;
            activeTarget = target;
            activeStartRotation = rightStartRotation;
            activeMaterial = rightMaterial;
            activeAxis = rightAxis;
            activeDirection = rightDirection;
        }
        else if (predictedClass == "left_hand")
        {
            activeArm = leftArm;
            activeHand = leftHand;
            activeTarget = leftTarget;
            activeStartRotation = leftStartRotation;
            activeMaterial = leftMaterial;
            activeAxis = leftAxis;
            activeDirection = leftDirection;
        }
        else
        {
            return;
        }

        activeCommand = predictedClass;
        angle = 0f;

        FindBestTargetAngle();

        if (targetAngle < 0.1f &&
            Vector3.Distance(activeHand.position, activeTarget.position)
            > targetDistance)
        {
            Debug.LogWarning(
                $"Мишень {activeCommand} недостижима при текущей оси вращения."
            );

            SetColor(activeMaterial, missColor);
            NotifyFeedback(activeCommand, "miss");
            activeCommand = "";
            return;
        }

        moving = true;
        targetReached = false;

        SetColor(activeMaterial, movingColor);

        NotifyFeedback(activeCommand, "moving");

        Debug.Log(
            $"Команда {activeCommand}. Угол: {targetAngle:F1}°"
        );
    }

    private Quaternion RotationAt(float candidate)
    {
        return activeStartRotation *
            Quaternion.AngleAxis(
                candidate * activeDirection,
                activeAxis.normalized
            );
    }

    private void FindBestTargetAngle()
    {
        Quaternion originalRotation = activeArm.localRotation;

        float bestDistance = float.MaxValue;
        targetAngle = 0f;

        const int samples = 100;

        for (int i = 0; i <= samples; i++)
        {
            float candidate = maxAngle * i / samples;

            activeArm.localRotation = RotationAt(candidate);

            float distance = Vector3.Distance(
                activeHand.position,
                activeTarget.position
            );

            if (distance < bestDistance)
            {
                bestDistance = distance;
                targetAngle = candidate;
            }
        }

        activeArm.localRotation = originalRotation;

        Debug.Log(
            $"Ближайшее положение {activeCommand}: {bestDistance:F3} ед."
        );
    }

    private void MoveToTarget()
    {
        angle = Mathf.MoveTowards(
            angle,
            targetAngle,
            moveSpeed * Time.deltaTime
        );

        activeArm.localRotation = RotationAt(angle);

        if (Mathf.Abs(angle - targetAngle) > 0.01f)
            return;

        moving = false;

        float distance = Vector3.Distance(
            activeHand.position,
            activeTarget.position
        );

        targetReached = distance <= targetDistance;

        if (targetReached)
        {
            repetitions++;

            SetColor(activeMaterial, successColor);
            NotifyFeedback(activeCommand, "success");

            Debug.Log(
                $"Касание {activeCommand} подтверждено: " +
                $"{repetitions}/{goalRepetitions}"
            );
        }
        else
        {
            SetColor(activeMaterial, missColor);
            NotifyFeedback(activeCommand, "miss");

            Debug.Log(
                $"Промах {activeCommand}. Расстояние: " +
                $"{distance:F3}. Не засчитано."
            );
        }

        BeginReturn(targetReached);
    }

    private void BeginReturn(bool wasSuccessful)
    {
        moving = false;
        returning = true;
        targetReached = wasSuccessful;
    }

    private void ReturnArm()
    {
        angle = Mathf.MoveTowards(
            angle,
            0f,
            moveSpeed * Time.deltaTime
        );

        activeArm.localRotation = RotationAt(angle);

        if (angle > 0.01f)
            return;

        angle = 0f;
        activeArm.localRotation = activeStartRotation;
        returning = false;

        SetColor(activeMaterial, waitingColor);

        if (targetReached && repetitions >= goalRepetitions)
        {
            completed = true;
            Debug.Log("Упражнение завершено!");
            NotifyFeedback(activeCommand, "completed");
        }
        else
        {
            NotifyFeedback("", "ready");
        }

        activeCommand = "";
        targetReached = false;
    }

    private void SetColor(Material material, Color color)
    {
        if (material != null)
            material.color = color;
    }

    public void SetEEGFeedback(float alphaPower)
    {
        // Цвет индикатора определяется состоянием упражнения.
    }

    private void ResetExercise()
    {
        moving = false;
        returning = false;
        completed = false;
        targetReached = false;
        repetitions = 0;
        angle = 0f;
        activeCommand = "";

        arm.localRotation = rightStartRotation;
        leftArm.localRotation = leftStartRotation;

        SetColor(rightMaterial, waitingColor);
        SetColor(leftMaterial, waitingColor);

        NotifyFeedback("", "ready");

        Debug.Log("Упражнение сброшено.");
    }
}
