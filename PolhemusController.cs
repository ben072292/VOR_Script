using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.IO;
using System;
using System.Collections.Generic;
using System.Text;

public class PolhemusController : MonoBehaviour
{
    //optional sliders
    public Slider divisor_slider;
    public Text divisor_value;
    public Slider sensors_slider;
    public Text sensors_value;

    //optional displays
    public Text displayValue;
    public Text fpsDisplay;

    

    //stream
    private PlStream plstream;

    //calibration values
    private Vector3 prime_position;
    private Vector4 calibrate_rotation;

    //public field that can be accessed by other classes
    public Vector3 angularSpeed; //regular angular speed calculated from direct Euler function
    public Quaternion quaternionSpeed; //radians Quaternion based  rotational speed average at any given time
    public Vector3 eulerQuaternionSpeed; //the Quaternion based rotational speed average at any given frame


    //internal storage of
    Vector3 oldPosition;
    Quaternion oldRotation;
    private StreamWriter file;
    Vector3 oldAngularDisplacement;

    //Quaternion angular change; Read quaternion Read with mouse movement changes
    public Quaternion quaternionRead;

    //quaternion representation of angular velocity in radians 
    public Quaternion diffQuaternion;

    //the Vector3 degrees representation of the angular velocity taken from the differentiation of the quaternion
    public Vector3 angularVelocityQuaternion;



    //histories used for averaging
    private Stack<Quaternion> rotationHistory;
    private Stack<Vector3> angularVelocityHistory;

    private float previousTime;
    private float[] cumulTime;
    private int counter;
    public int counterLimit = 10;

    private int logCounter;
    public int logCounterLimit = 0;
    private bool logging;

    public int startCounter = 0;
    private int logStart;




    // Use this for initialization
    void Awake()
    {
        // set divisor defaults
        divisor_slider.value = 1.0f;

        // set sensors defaults
        sensors_slider.value = 1;

        // get the stream component
        plstream = GetComponent<PlStream>();


        // set sensors_slider max value
        sensors_slider.maxValue = plstream.active.Length;
    }

    void Start()
    {
        // initializes arrays, fixes positions
        calibrate_rotation = new Vector4();
        oldPosition = new Vector3();
        oldRotation = new Quaternion();
        file = new System.IO.StreamWriter("log.txt");
        zero();

        oldAngularDisplacement = new Vector3();
        counter = 0;
        logCounter = 0;

        previousTime = 0;
        cumulTime = new float[counterLimit];
        rotationHistory = new Stack<Quaternion>();
        //initializes stack history with 0's
        for (int i = 0; i < counterLimit; i++)
        {
            rotationHistory.Push(new Quaternion());
        }

        //initializes stack history with 0's
        angularVelocityHistory = new Stack<Vector3>();
        for (int i = 0; i < 5; i++)
        {
			angularVelocityHistory.Push(new Vector3());
        }

        startCounter = 0;
        logStart = 0;

        //intializing readable quaternions
        quaternionRead = new Quaternion();
        quaternionSpeed = new Quaternion();
        angularVelocityQuaternion = new Vector3();
        diffQuaternion = new Quaternion();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown("escape"))
        {
            //Application.Quit();
        }
    }

    // called before performing any physics calculations
    void FixedUpdate()
    {
        // update divisor text
        divisor_value.text = divisor_slider.value.ToString("F1");

        // for each knuckle up to sensors slider value, update the position
        for (int i = 0; plstream != null && i < plstream.active.Length; ++i)
        {
            if (plstream.active[i])
            {
                /*
                transfers plstream translated bit information to unity engine parameters
                */
                Vector3 pol_position = plstream.positions[i] - prime_position;
                Vector4 pol_rotation = plstream.orientations[i] //-calibrate_rotation;
                    ;

                // doing crude (90 degree) rotations into frame
                Vector3 unity_position;
                unity_position.x = pol_position.y;
                unity_position.y = -pol_position.z;
                unity_position.z = pol_position.x;


                Quaternion unity_rotation;
                unity_rotation.w = pol_rotation[0];
                unity_rotation.x = -pol_rotation[2];
                unity_rotation.y = pol_rotation[3];
                unity_rotation.z = -pol_rotation[1];
                //unity_rotation = Quaternion.Inverse(unity_rotation);

                Quaternion rcalibration;
                rcalibration.w = calibrate_rotation[0];
                rcalibration.x = -calibrate_rotation[2];
                rcalibration.y = calibrate_rotation[3];
                rcalibration.z = -calibrate_rotation[1];

                //recalibrated rotation
                unity_rotation = unity_rotation * Quaternion.Inverse(rcalibration);




                if (plstream.digio[i] != 0)
                {
                    zero();
                }

                //angular speed
                float angleInDegrees;
                Vector3 rotationAxis;
                unity_rotation.ToAngleAxis(out angleInDegrees, out rotationAxis);
                float deltaTime = Time.deltaTime;

                Vector3 angularDisplacement = rotationAxis * angleInDegrees // * Mathf.Deg2Rad
                    ;
                Vector3 angularSpeed = angularDisplacement / deltaTime;


                //Quaternion differentiation
                diffQuaternion = quaternionDerivative(unity_rotation, oldRotation, deltaTime);
                diffQuaternion = diffQuaternion * Quaternion.Inverse(unity_rotation);

                

                //d q(t) /dt = ½ * W(t) q(t)
                //The equation for angular velocity from quaternions is 
                // W(t)  = 2 *  d  q(t)/dt * q^-1(t)
                //where W(t) is teh angular veloctiy
                int motion = (int)(diffQuaternion.y * Mathf.Rad2Deg);

                //returns angular velocity in degrees
                angularVelocityQuaternion = averageQuaternionSpeed(diffQuaternion.eulerAngles, angularVelocityHistory);

                //determines when to write values to displays
                if ((plstream.positions[i].x != oldPosition.x) && (plstream.positions[i].x != 0))
                {
                    if ((counter >= 0) && (counter < cumulTime.Length)) {
                        cumulTime[counter] = previousTime;
                        previousTime = 0;
                    }
                    else {
                        previousTime += deltaTime;
                    }
                    counter++;
                    oldPosition = unity_position;
                }
                else
                {
                    previousTime += deltaTime;
                }

                if (plstream.orientations[i].x != oldRotation.x)
                {
                    if ((counterLimit > 0) && (diffQuaternion.y != 0f))
                    {
                        rotationHistory.Pop();
                        rotationHistory.Push(diffQuaternion);
                    }
                }



                //update old values
                oldRotation = unity_rotation;
                oldAngularDisplacement = angularDisplacement;


                //displays values to text fields when storage limit reached
                if ((counter >= counterLimit))
                {
                    //display fps
                    float total = 0;
                    foreach (float t in cumulTime)
                    {
                        total += t;
                    }
                    float fps = counterLimit / total;
                    if (fpsDisplay != null)
                    {
                        fpsDisplay.text = "" + (int)fps;
                    }



                    //display angular speed from quaternion differentiation
                    float totalSpeedsX = 0f;
                    float totalSpeedsY = 0f;
                    float totalSpeedsZ = 0f;
                    Stack<Quaternion> temp = new Stack<Quaternion>(rotationHistory);
                    foreach (Quaternion unit in temp)
                    {
                        totalSpeedsX += unit.x * Mathf.Rad2Deg;
                    }

                    foreach (Quaternion unit in temp)
                    {
                        totalSpeedsY += unit.y * Mathf.Rad2Deg;
                    }
                    foreach (Quaternion unit in temp)
                    {
                        totalSpeedsZ += unit.z * Mathf.Rad2Deg;
                    }

                    if (displayValue != null)
                    {
                        //displayValue.text = "y: " + motion;
                        displayValue.text = "x: " + (int)(totalSpeedsX / counterLimit) + "y: " + (int)(totalSpeedsY / counterLimit) + "z: " + (int)(totalSpeedsZ / counterLimit);
                    }
                    eulerQuaternionSpeed = new Vector3(totalSpeedsX, totalSpeedsY, totalSpeedsZ);

                    //reset counter
                    counter = 0;
                }




                /**
                section
                writes log data to file 
                */
                if ((startCounter >= logStart) && (logCounter < logCounterLimit))
                {
                    StringBuilder logdata = new StringBuilder();
                    logdata.Append("Time: ");
                    logdata.Append(1f / deltaTime);
                    logdata.Append(" Positions:  x: ");
                    logdata.Append(plstream.positions[i].x);
                    logdata.Append(" y: ");
                    logdata.Append(plstream.positions[i].y);
                    logdata.Append(" z: ");
                    logdata.Append(plstream.positions[i].z);

                    /*
                    logdata.Append("         Quaternions: w: ");
                    logdata.Append(diffQuaternion.w);
                    logdata.Append(" x: ");
                    logdata.Append(diffQuaternion.x);
                    logdata.Append(" y: ");
                    logdata.Append(diffQuaternion.y);
                    logdata.Append(" z: ");
                    logdata.Append(diffQuaternion.z);
                    file.WriteLine(logdata.ToString());
                    logCounter++;
                    */


                    logdata.Append("         Orientations: w: ");
                    logdata.Append(pol_rotation.w);
                    logdata.Append(" x: ");
                    logdata.Append(pol_rotation.x);
                    logdata.Append(" y: ");
                    logdata.Append(pol_rotation.y);
                    logdata.Append(" z: ");
                    logdata.Append(pol_rotation.z);
                    file.WriteLine(logdata.ToString());
                    logCounter++;

                }
                else
                    startCounter++;
            }

        }
    }

    //gets the change of the quaternion over time 
    private Quaternion quaternionDerivative(Quaternion newQ, Quaternion oldQ, float deltaTime)
    {


        float oldw = oldQ.w;
        float oldx = oldQ.x;
        float oldy = oldQ.y;
        float oldz = oldQ.z;

        float newW = newQ.w;
        float newx = newQ.x;
        float newy = newQ.y;
        float newz = newQ.z;

        float diffw = 2 * (newW - oldw) / deltaTime;
        float diffx = 2 * (newx - oldx) / deltaTime;
        float diffy = 2 * (newy - oldy) / deltaTime;
        float diffz = 2 * (newz - oldz) / deltaTime;


        Quaternion dQ = new Quaternion(diffw, diffx, diffy, diffz);
        quaternionRead = dQ;

        return dQ;
    }

    //gets the timed average of the quaternion set it is given; 
    //calculates the average based on the new quaternion speed it is given at the current instance
    //form of euler angle returned is the angular velocity in degrees
    private Vector3 averageQuaternionSpeed(Vector3 newQ, Stack<Vector3> quaternionHistory)
    {
        quaternionHistory.Pop();
        quaternionHistory.Push(newQ);


        float totalSpeedsX = 0f;
        float totalSpeedsY = 0f;
        float totalSpeedsZ = 0f;
        Stack<Vector3> temp = new Stack<Vector3>(quaternionHistory);
        foreach (Vector3 unit in temp)
        {
            totalSpeedsX += unit.x * Mathf.Rad2Deg;
            totalSpeedsY += unit.y * Mathf.Rad2Deg;
            totalSpeedsZ += unit.z * Mathf.Rad2Deg;
        }
        float avgX = totalSpeedsX / temp.Count;
        float avgY = totalSpeedsY / temp.Count;
        float avgZ = totalSpeedsZ / temp.Count;

        return new Vector3(avgX, avgY, avgZ);
    }
    

    //fixes position
    public void zero()
    {

        for (var i = 0; i < plstream.active.Length; ++i)
        {
            if (plstream.active[i])
            {
                prime_position = plstream.positions[i];
                break;
            }
        }
    }

    //fixes rotation
    public void calibrate()
    {
        for (var i = 0; i < plstream.active.Length; ++i)
        {
            if (plstream.active[i])
            {
                calibrate_rotation = plstream.orientations[i];
                break;
            }
        }
    }

    //gets angular speed using the quaternion differentiation
    public Vector3 getEulerRotationalSpeed()
    {
        return this.eulerQuaternionSpeed;
    }

    //gets the fixed position of the polhemus tracker
    public Vector3 getPosition()
    {
        return this.oldPosition;
    }

    //gets the 3 component angular rotation from the Quaternion differentiation
    public Vector3 getQuaternionRotationalSpeed()
    {

        return new Vector3();
    }
}