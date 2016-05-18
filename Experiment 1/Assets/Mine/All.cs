﻿using UnityEngine;
using System;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Collections.Generic;

using Marker = System.Collections.Generic.KeyValuePair<UnityEngine.Vector3, int>;

public class All : MonoBehaviour {

    //  Game objects
    public GameObject gIndexTop;
    public GameObject gThumbTop;
    public GameObject gDisplay;
    public GameObject gScreen;
    public GameObject gTaskSource;
    public GameObject gTaskTarget;

    //  Network
    TcpClient socket = new TcpClient();
    const string serverIP = "192.168.0.109";
    const int serverPort = 7643;

    //  Scene
    const float ADJUST_DELTA = 0.001f;
    Task task;
    int taskId = -1;

    //  Track
    Tracking tracking = new Tracking();
    int[] markersPerFinger = new int[] { 3, 4 };
    Vector3 handBias = new Vector3(0.0f, 0.0f, 0.0f);
    bool toRegister;
    int frameID;
    Frame now = null;
    
    void Start() {
        try
        {
            socket.Connect(serverIP, serverPort);
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
        if (socket.Connected == true)
        {
            Thread receiveThread = new Thread(ReceiveThread);
            receiveThread.Start();
        }
        
        tracking.markersPerFinger = markersPerFinger;
        
        gThumbTop = GameObject.Find("sphere (2)");
        gIndexTop = GameObject.Find("sphere (6)");
        gDisplay = GameObject.Find("display");
        gScreen = GameObject.Find("screen");
        gTaskSource = GameObject.Find("task source");
        gTaskTarget = GameObject.Find("task target");
        
        ChangeTask();
    }
    
    void Update()
    {
        //  keyboard event
        KeyboardEvent();

        //  tracking
        if (now != null && now.used == false)
        {
            Debug.Log(now.n);
            now.used = true;
            now = tracking.Track(now, toRegister);
            int fingerTop = markersPerFinger[0];
            for (int i = 0, j = 0; i < now.n; i++)
            {
                GameObject g = GameObject.Find("sphere (" + i + ")");
                g.transform.position = now.pl[i].Key;
                if (i == fingerTop - 1)
                {
                    g.GetComponent<Renderer>().material.color = Color.black;
                    fingerTop += markersPerFinger[++j];
                }
            }
            for (int i = 0; i < 4; i++)
            {
                GameObject g = GameObject.Find("sphere (" + (now.n + i) + ")");
                g.transform.position = now.rb[i].Key;
                g.GetComponent<Renderer>().material.color = Color.gray;
            }
        }

        //  task
        task.Work();
    }

    void KeyboardEvent()
    {
        //  adjust screen
        if (Input.GetKey(KeyCode.W)) gDisplay.transform.Translate(0.0f, ADJUST_DELTA, 0.0f);
        if (Input.GetKey(KeyCode.S)) gDisplay.transform.Translate(0.0f, -ADJUST_DELTA, 0.0f);
        if (Input.GetKey(KeyCode.A)) gDisplay.transform.Translate(-ADJUST_DELTA, 0.0f, 0.0f);
        if (Input.GetKey(KeyCode.D)) gDisplay.transform.Translate(ADJUST_DELTA, 0.0f, 0.0f);
        if (Input.GetKey(KeyCode.Q)) gDisplay.transform.Translate(0.0f, 0.0f, ADJUST_DELTA);
        if (Input.GetKey(KeyCode.E)) gDisplay.transform.Translate(0.0f, 0.0f, -ADJUST_DELTA);

        //  adjust hand
        if (Input.GetKey(KeyCode.I)) handBias += new Vector3(0.0f, ADJUST_DELTA, 0.0f);
        if (Input.GetKey(KeyCode.K)) handBias += new Vector3(0.0f, -ADJUST_DELTA, 0.0f);
        if (Input.GetKey(KeyCode.J)) handBias += new Vector3(-ADJUST_DELTA, 0.0f, 0.0f);
        if (Input.GetKey(KeyCode.L)) handBias += new Vector3(ADJUST_DELTA, 0.0f, 0.0f);
        if (Input.GetKey(KeyCode.U)) handBias += new Vector3(0.0f, 0.0f, ADJUST_DELTA);
        if (Input.GetKey(KeyCode.O)) handBias += new Vector3(0.0f, 0.0f, -ADJUST_DELTA);

        //  register
        toRegister = false;
        if (Input.GetKey(KeyCode.BackQuote)) toRegister = true;

        //  change task
        if (Input.GetKey(KeyCode.T)) ChangeTask();
    }

    void ChangeTask()
    {
        taskId = (taskId + 1) % 3;
        switch (taskId)
        {
            case 0:
                task = new TaskClick(this);
                break;
            case 1:
                task = new TaskDrag(this);
                break;
            case 2:
                task = new TaskZoom(this);
                break;
        }
    }
    
    void ReceiveThread()
    {
        Debug.Log("Network receiving");
        StreamReader streamReader = new StreamReader(socket.GetStream());
        Frame tmp = null;
        while (true)
        {
            string line = streamReader.ReadLine();
            if (line == null) break;
            //Debug.Log(line);
            string[] arr = line.Split(' ');
            switch (arr[0])
            {
                case "framestart":
                    frameID += 1;
                    //Debug.Log(frameID);
                    tmp = new Frame();
                    break;
                case "frameend":
                    now = tmp;
                    break;
                case "rbposition":
                    tmp.rb.Add(new Marker(new Vector3(float.Parse(arr[1]), -float.Parse(arr[2]), float.Parse(arr[3])) / 2 + handBias, frameID));
                    break;
                case "rbrotation":
                    tmp.rbRotation = new Vector4(float.Parse(arr[1]), float.Parse(arr[2]), float.Parse(arr[3]), float.Parse(arr[4]));
                    break;
                case "othermarker":
                    tmp.Add(new Marker(new Vector3(float.Parse(arr[1]), -float.Parse(arr[2]), float.Parse(arr[3])) / 2 + handBias, frameID));
                    break;
            }
        }
        Debug.Log("Network disconnect");
    }
}


class Task
{
    const float TOUCH_DIST_Z = 0.02f;
    public Color colorIdle = new Color(1.00f, 0.58f, 0.58f);
    public Color colorWork = new Color(1.00f, 0.16f, 0.16f);
    public All a;
    public GameObject gIndexTop;
    public GameObject gThumbTop;
    public GameObject gScreen;
    public GameObject gTaskSource;
    public GameObject gTaskTarget;
    public List<Vector3> poss = new List<Vector3>();

    public Task(All a)
    {
        this.a = a;
        gIndexTop = a.gIndexTop;
        gThumbTop = a.gThumbTop;
        gScreen = a.gScreen;
        gTaskSource = a.gTaskTarget;
        gTaskTarget = a.gTaskTarget;
        for (int i = -2; i <= 2; i++)
            for (int j = -1; j <= 1; j++)
            {
                float x = i / 6.0f * gScreen.transform.localScale.x;
                float y = j / 3.0f * gScreen.transform.localScale.y;
                poss.Add(new Vector3(x, y, 0.0f));
            }
    }

    public virtual void Work() { }

    public int Random(int x)
    {
        return new System.Random(DateTime.Now.Millisecond).Next() % x;
    }

    public bool IsTouch(GameObject g0, GameObject g1, float b = 2)
    {
        float distX = Math.Abs(g0.transform.position.x - g1.transform.position.x) * b;
        float distY = Math.Abs(g0.transform.position.y - g1.transform.position.y) * b;
        float distZ = Math.Abs(g0.transform.position.z - g1.transform.position.z);
        return (distX < g0.transform.lossyScale.x) && (distY < g0.transform.lossyScale.y) && (distZ < TOUCH_DIST_Z);
    }
}

class TaskClick : Task
{
    int lastPos = -1;

    public TaskClick(All a) : base(a)
    {
        gTaskSource.SetActive(false);
        gTaskTarget.SetActive(true);
        LocateOnScreen();
    }

    public override void Work()
    {
        if (IsTouch(gTaskTarget, gIndexTop, 4))
        {
            Debug.Log("click " + DateTime.Now);
            LocateOnScreen();
        }
    }

    void LocateOnScreen()
    {
        int i = Random(poss.Count);
        while (i == lastPos) i = Random(poss.Count);
        gTaskTarget.transform.localPosition = new Vector3(poss[i].x, poss[i].y, gTaskTarget.transform.localPosition.z);
        lastPos = i;
    }
}

class TaskDrag : Task
{
    const float DRAG_SOURCE_TARGET_DIST = 0.01f;
    int dragStep = 0;
    int dragTargetPos;
    DateTime dragTime;
    Vector3 dragDist;

    public TaskDrag(All a) : base(a)
    {
        gTaskSource.SetActive(true);
        gTaskTarget.SetActive(true);
        LocateOnScreen();
    }

    public override void Work()
    {
        if (IsTouch(gTaskSource, gIndexTop))
        {
            switch (dragStep)
            {
                case 0:
                    dragStep = 1;
                    dragTime = DateTime.Now;
                    break;
                case 1:
                    if (DateTime.Now - dragTime > TimeSpan.FromSeconds(0.1))
                    {
                        dragStep = 2;
                        Debug.Log("drag " + dragTime);
                        gTaskSource.GetComponent<Renderer>().material.color = colorWork;
                        dragDist = gTaskSource.transform.position - gIndexTop.transform.position;
                    }
                    break;
                case 2:
                    float x = gIndexTop.transform.position.x + dragDist.x;
                    float y = gIndexTop.transform.position.y + dragDist.y;
                    float z = gTaskSource.transform.position.z;
                    gTaskSource.transform.position = new Vector3(x, y, z);
                    break;
            }
        }
        else
        {
            if ((gTaskSource.transform.position - gTaskTarget.transform.position).magnitude < DRAG_SOURCE_TARGET_DIST)
            {
                LocateOnScreen();
            }
            dragStep = 0;
            gTaskSource.GetComponent<Renderer>().material.color = colorIdle;
        }
    }

    void LocateOnScreen()
    {
        int i = Random(poss.Count);
        gTaskSource.transform.localPosition = new Vector3(poss[i].x, poss[i].y, gTaskSource.transform.localPosition.z);
        int j = Random(poss.Count);
        while (j == i) j = Random(poss.Count);
        gTaskTarget.transform.localPosition = new Vector3(poss[i].x, poss[i].y, gTaskTarget.transform.localPosition.z);
    }
}

class TaskZoom : Task
{
    const float ZOOM_SOURCE_TARGET_SIZE_DIST = 0.01f;
    int zoomStep = 0;
    DateTime zoomTime;
    float zoomDist;
    float zoomScale;

    public TaskZoom(All a) : base(a)
    {
        gTaskSource.SetActive(true);
        gTaskTarget.SetActive(true);
        LocateOnScreen();
    }

    public override void Work()
    {
        if (gTaskSource.activeSelf == false) return;
        if (IsTouch(gTaskSource, gThumbTop) && IsTouch(gTaskSource, gIndexTop))
        {
            switch (zoomStep)
            {
                case 0:
                    zoomStep = 1;
                    zoomTime = DateTime.Now;
                    break;
                case 1:
                    if (DateTime.Now - zoomTime > TimeSpan.FromSeconds(0.2))
                    {
                        zoomStep = 2;
                        Debug.Log("zoom " + zoomTime);
                        gTaskSource.GetComponent<Renderer>().material.color = colorWork;
                        zoomDist = (gThumbTop.transform.position - gIndexTop.transform.position).magnitude;
                        zoomScale = gTaskSource.transform.localScale.x;
                    }
                    break;
                case 2:
                    float d = (gThumbTop.transform.position - gIndexTop.transform.position).magnitude;
                    float s = d / zoomDist * zoomScale;
                    gTaskSource.transform.localScale = new Vector3(s, s, gTaskSource.transform.localScale.z);
                    break;
            }
        }
        else
        {
            if (Math.Abs(gTaskSource.transform.lossyScale.x - gTaskTarget.transform.lossyScale.x) < ZOOM_SOURCE_TARGET_SIZE_DIST)
            {
                LocateOnScreen();
            }
            zoomStep = 0;
            gTaskSource.GetComponent<Renderer>().material.color = colorIdle;
        }
    }

    void LocateOnScreen()
    {
        float s0 = Random(5) / 100.0f + 0.04f;
        float s1 = Random(6) / 100.0f + s0 + 0.03f;
        gTaskSource.transform.localScale = new Vector3(s0, s0, 0.01f);
        gTaskTarget.transform.localScale = new Vector3(s1, s1, 0.01f);
    }
}


class Tracking
{
    const int MAX_MARKER_NUM = 30;
    const float NOISE_DIST = 0.3f;
    public int[] markersPerFinger;
    Frame now;
    Frame last = null;
    int frameID = -1;

    public Frame Track(Frame newFrame, bool toRegister = false)
    {
        now = newFrame;
        if (last == null || toRegister)
        {
            Register();
        }
        else
        {
            Referring();
        }
        last = now;
        return now;
    }
    
    void Register()
    {
        for (int i = now.n - 1; i >= 0; i--)
        {
            if ((now.pl[i].Key - now.rb[0].Key).magnitude > NOISE_DIST)
            {
                now.pl.RemoveAt(i);
            }
        }

        // init and get normal vector
        int[] arr = new int[now.n];
        for (int i = 0; i < now.n; i++) arr[i] = i;
        Vector3 nv = Vector3x.NormalVector(now.rb[1].Key, now.rb[2].Key, now.rb[3].Key);

        // get angle and dist
        float[] angle = new float[now.n];
        float[] dist = new float[now.n];
        for (int i = 0; i < now.n; i++)
        {
            Vector3 pv = Vector3x.ProjectiveVector(nv, now.pl[i].Key - now.rb[0].Key);
            angle[i] = Vector3.Angle(pv, now.rb[1].Key - now.rb[0].Key);
            dist[i] = (now.pl[i].Key - now.rb[0].Key).magnitude;
        }

        // sort by finger then sort by dist per finger
        Array.Sort(arr, (int a, int b) => { return angle[a] < angle[b] ? -1 : 1; });
        int stride = 0;
        for (int finger = 0; finger < markersPerFinger.Length; finger++)
        {
            int markerN = markersPerFinger[finger];
            int[] arr2 = new int[markerN];
            for (int i = 0; i < markerN; i++) arr2[i] = arr[stride + i];
            Array.Sort(arr2, (int a, int b) => { return dist[a] < dist[b] ? -1 : 1; });
            for (int i = 0; i < markerN; i++) arr[stride + i] = arr2[i];
            stride += markerN;
        }

        // renew
        List<Marker> pl2 = new List<Marker>();
        for (int i = 0; i < now.n; i++)
        {
            pl2.Add(now.pl[arr[i]]);
        }
        now.pl = pl2;
    }

    void Referring()
    {
        // matching
        NetworkFlow networkFlow = new NetworkFlow(last.n, now.n);
        float[,] weight = new float[now.n, last.n];
        for (int i = 0; i < now.n; i++)
        {
            int[] arr = new int[last.n];
            for (int j = 0; j < last.n; j++)
            {
                weight[i, j] = (now.pl[i].Key - last.pl[j].Key).magnitude;
                arr[j] = j;
            }
            Array.Sort(arr, (int a, int b) => { return weight[i, a] < weight[i, b] ? -1 : 1; });
            for (int j = 0; j < Math.Min(last.n, 5); j++)
            {
                int k = arr[j];
                networkFlow.AddEdge(k, i, weight[i, k]);
            }
        }
        int[] match = networkFlow.Solve();
        Frame fix = new Frame();
        fix.rb = now.rb;
        for (int i = 0; i < last.n; i++)
        {
            if (match[i] == -1)
            {
                fix.Add(last.pl[i]);
            }
            else
            {
                fix.Add(now.pl[match[i]]);
            }
        }
        now = fix;
    }
}

class Vector3x
{
    public static Vector3 nan = Vector3.one * 1e20f;

    public static bool Equal(Vector3 a, Vector3 b)
    {
        if ((a - b).magnitude < 4e-3) return true;
        return false;
    }

    public static Vector3 NormalVector(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a);
    }

    public static Vector3 ProjectiveVector(Vector3 nv, Vector3 a)
    {
        float len = Vector3.Dot(nv.normalized, a);
        return a - nv.normalized * len;
    }
}

class Frame
{
    public List<Marker> rb;
    public Vector4 rbRotation;
    public List<Marker> pl;
    public int n
    {
        get { return pl.Count; }
    }
    public bool used;

    public Frame()
    {
        rb = new List<Marker>();
        rbRotation = new Vector4(0, 0, 0, 0);
        pl = new List<Marker>();
        used = false;
    }
    
    public void Add(Marker p)
    {
        pl.Add(p);
    }
}

class NetworkFlow
{
    const int INF = 0xfffffff;
    List<int>[] edge;
    List<int> ev;
    List<int> ec;
    List<float> eq;
    int n;
    int nLast;
    int s;
    int t;
    public NetworkFlow(int nLast, int nNow)
    {
        this.nLast = nLast;
        n = nLast + nNow + 2;
        s = n - 2;
        t = n - 1;
        edge = new List<int>[n];
        for (int i = 0; i < n; i++) edge[i] = new List<int>();
        ev = new List<int>();
        ec = new List<int>();
        eq = new List<float>();
        for (int i = 0; i < nLast; i++) AddEdge(s, i, 1, 0);
        for (int i = 0; i < nNow; i++) AddEdge(nLast + i, t, 1, 0);
    }

    public void AddEdge(int u, int v, float q)
    {
        AddEdge(u, nLast + v, 1, q);
    }
    public void AddEdge(int u, int v, int c, float q)
    {
        edge[u].Add(ev.Count);
        ev.Add(v);
        ec.Add(c);
        eq.Add(q);
        edge[v].Add(ev.Count);
        ev.Add(u);
        ec.Add(0);
        eq.Add(-q);
    }

    public int[] Solve()
    {
        while (true)
        {
            float[] dist = new float[n];
            bool[] visit = new bool[n];
            int[] pref = new int[n];
            for (int i = 0; i < n; i++) dist[i] = INF;
            dist[s] = 0;
            visit[s] = true;
            List<int> que = new List<int>();
            que.Add(s);
            for (int quefi = 0; quefi < que.Count; quefi++)
            {
                int u = que[quefi];
                for (int i = 0; i < edge[u].Count; i++)
                {
                    int j = edge[u][i];
                    if (ec[j] <= 0) continue;
                    int v = ev[j];
                    float q = eq[j];
                    if (dist[v] <= dist[u] + q) continue;
                    dist[v] = dist[u] + q;
                    pref[v] = j;
                    if (visit[v]) continue;
                    visit[v] = true;
                    que.Add(v);
                }
                visit[u] = false;
            }
            if (dist[t] >= INF) break;
            for (int i = t; i != s; i = ev[pref[i] ^ 1])
            {
                ec[pref[i]] -= 1;
                ec[pref[i] ^ 1] += 1;
            }
        }
        int[] match = new int[nLast];
        for (int u = 0; u < nLast; u++)
        {
            match[u] = -1;
            for (int i = 0; i < edge[u].Count; i++)
            {
                int j = edge[u][i];
                if (ec[j] == 0 && ev[j] < s)
                {
                    match[u] = ev[j] - nLast;
                    break;
                }
            }
        }
        return match;
    }
}