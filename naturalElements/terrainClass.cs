using System;
using System.Collections.Generic;
using System.Diagnostics;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;

public class terrain
{
    //this is the constructor of this class which initializes a new instance of the object
    //using provided surface object and the document instance
    //the document instance is passed so that objects can be added or removed from the document
    //from within this class
	public terrain(NurbsSurface surface,double height, ref RhinoDoc doc)
	{
        //the surface that is passed into the constructor is being assigned to the property
        currentDoc = doc;
        terrainHeight = height;
        terrainSurface = surface;
        originalSurface = surface;

        surfaceBoundBox = surface.GetBoundingBox(false);
        basePoint = surfaceBoundBox.PointAt(0.5,0.5,0);
	}

    //this is the terrainSurface method, which is a 'NurbsCurve' object corresponding to the terrain geometry
    public NurbsSurface terrainSurface
    {
        get;
        private set;
    }
    //we are storing the geometry of the original surface here
    public NurbsSurface originalSurface
    {
        get;
        private set;
    }
    //we are storing the guid of the created terrain that is added to the document
    public Guid surfaceGuid
    {
        private set;
        get;
    }
    //this is the ObjRef - reference to the created terrain that is added to the document
    public Rhino.DocObjects.ObjRef surfaceRef
    {
        get;
        private set;
    }
    //this is the bounding box of the created terrain
    public BoundingBox surfaceBoundBox
    {
        get;
        private set;
    }
    //this is the base point of the created terrain which is calculated as the
    //center of the bottom face of the bouding box
    public Point3d basePoint
    {
        get;
        private set;
    }
    //this will contain the instance of the doc object - gives control over the 
    //rhino document from within the class
    private RhinoDoc currentDoc;
    //the height of the terrain
    public double terrainHeight;
    //the rate at which the undulations decay from generation to generation
    private double decayRate = 0.769;

    //this method move the controlPoint of the terrainSurface (of this object instance)
    //u and v are the indices of the controlPoints which will be moves by a translation vector transVec
    private void moveControlPoint(int u, int v, Vector3d transVec)
    {
        ControlPoint ctlpt = terrainSurface.Points.GetControlPoint(u, v);
        Point3d ptLocation = ctlpt.Location;
        ptLocation += transVec;
        ctlpt.Location = ptLocation;
        terrainSurface.Points.SetControlPoint(u, v, ctlpt);
    }

    //this method scales the terrain surface along the given direction by the given factor
    //the scaling is one dimensional
    public void Scale1D(Point3d basePt, Vector3d scaleDirection, double scaleFactor)
    {
        if(scaleFactor == 0)
        {
            terrainSurface = originalSurface;
            return;
        }

        for(int u = 0; u < terrainSurface.Points.CountU; u++)
        {
            for(int v = 0; v < terrainSurface.Points.CountV; v++)
            {
                ControlPoint cPt = terrainSurface.Points.GetControlPoint(u, v);
                Point3d cptLocation = cPt.Location;
                if(scaleDirection.Length != 0)scaleDirection.Unitize();
                
                Vector3d posVec = cptLocation - basePt;
                Vector3d component = scaleDirection * (posVec * scaleDirection);
                Point3d localBase = cptLocation - component;
                component = component * scaleFactor;
                cptLocation = localBase + component;

                cPt.Location = cptLocation;
                terrainSurface.Points.SetControlPoint(u, v, cPt);
            }
        }
    }

    //this is a recursive method that creates the undulations in the terrain
    public void makeTerrain(int genNum = 1)
    {
        int count;
        double startHeight = terrainHeight*(1-decayRate);
        double curHeight;
        int curGen = 0;
        Random toss = new Random();

        while(curGen < genNum)
        {
            //calculating the number of control points in each direction
            count = (2 * (curGen+1)) + 3+2;
            //rebuilding the surface with the above number of control points
            terrainSurface = terrainSurface.Rebuild(2, 2, count, count);

            //creating undulations
            int uBuffer = 3;
            int vBuffer = 3;

            int u = uBuffer;
            while(u < count-uBuffer)
            {
                int v = vBuffer;
                while (v < count-vBuffer)
                {
                    curHeight = startHeight * Math.Pow(decayRate, curGen);
                    double decide = toss.NextDouble();
                    if (decide > 0.5 && curGen != 0) curHeight = -curHeight;
                    //Debug.WriteLine(curHeight.ToString());
                    moveControlPoint(u, v, new Vector3d(0, 0, curHeight));
                    
                    v += 1;
                }
                u += 1;
            }
            curGen++;
        }
    }

    //this method marks all the control Points of the surface in the document
    public Result markControlPoints()
    {
        for(int u = 0; u < terrainSurface.Points.CountU; u++)
        {
            for(int v = 0; v < terrainSurface.Points.CountV; v++)
            {
                ControlPoint controlPt = terrainSurface.Points.GetControlPoint(u, v);
                currentDoc.Objects.AddPoint(controlPt.Location);
            }
        }
        return Result.Success;
    }

    //this method renders the terrain..
    //which means it adds the surface to the document
    public void render()
    {
        surfaceGuid = currentDoc.Objects.AddSurface(terrainSurface);
        surfaceRef = new Rhino.DocObjects.ObjRef(surfaceGuid);
        Debug.WriteLine("I am here");
        Rhino.DocObjects.RhinoObject surfaceObj = surfaceRef.Object();
        surfaceObj.Attributes.ObjectColor = System.Drawing.Color.ForestGreen;
        surfaceObj.Attributes.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
        surfaceObj.CommitChanges();

        surfaceBoundBox = terrainSurface.GetBoundingBox(false);
    }
}
