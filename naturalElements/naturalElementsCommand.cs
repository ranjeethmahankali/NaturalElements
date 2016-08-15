using System;
using System.Collections.Generic;
using System.Diagnostics;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.ApplicationSettings;

namespace naturalElements
{
    [System.Runtime.InteropServices.Guid("7dd4929b-a0eb-4743-aebf-fc31f9d1a96c")]
    public class naturalTerrainCommand : Command
    {
        public naturalTerrainCommand()
        {
            // Rhino only creates one instance of each command class defined in a
            // plug-in, so it is safe to store a refence in a static property.
            Instance = this;
        }

        ///<summary>The only instance of this command.</summary>
        public static naturalTerrainCommand Instance
        {
            get; private set;
        }
        
        ///<returns>The command name as it appears on the Rhino command line.</returns>
        public override string EnglishName
        {
            get { return "naturalTerrain"; }
        }

        //this is the code that gets executed when the command is run-- the code inside
        //the RunCommand method
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {   
            //this is the urbs surface Object in which the user selected surface will be stored
            NurbsSurface terrSurf;
            //this is the object reference to the original surface object, so that it can be dlted later
            Rhino.DocObjects.ObjRef originalRef;
            //now we are asking the user to select the flat surface
            using(GetObject surfSelect = new GetObject())
            {
                surfSelect.SetCommandPrompt("Select the surface");
                //setting the filter so that user only selects surfaces
                surfSelect.GeometryFilter = Rhino.DocObjects.ObjectType.Surface;
                if(surfSelect.Get() != GetResult.Object)
                {
                    //the selection was invalid so we are displaying a message and exiting
                    surfSelect.SetCommandPrompt("Invalid Selection");
                    return Result.Failure;
                }
                //storing the user selected surface and its reference in these variables
                //that we created previously
                terrSurf = surfSelect.Object(0).Surface().ToNurbsSurface();
                originalRef = surfSelect.Object(0);
            }

            //the following is when we want to ask the user to provide the generation count
            /*
            using (GetInteger genNumInput = new GetInteger())
            {
                genNumInput.SetCommandPrompt("Enter the number of generation");
                if(genNumInput.Get() != GetResult.Number)
                {
                    RhinoApp.WriteLine("Error! invalid number");
                    return Result.Failure;
                }

                generationCount = genNumInput.Number();
            }
            */

            //creating the terrain with zero height for now
            terrain finalTerrain = new terrain(terrSurf, 0, ref doc);

            bool deleted = doc.Objects.Delete(originalRef, true);
            if (!deleted)
            {
                RhinoApp.WriteLine("Error while deleting!");
                return Result.Failure;
            }

            int generationCount = 10;

            //Now the user is selecting the terrain height
            Point3d heightPoint;
            using(GetPoint ptSelect = new GetPoint())
            {
                //this is the reference height usign which a temporary terrain is generated.
                double refHeight = 5;
                //this is the temporary terrain that is created whose purpose is merely to show the user
                //a preview of terrain for the height estimate
                terrain tempTerrain = new terrain(terrSurf, refHeight, ref doc);
                tempTerrain.makeTerrain(generationCount);

                //we are turning te osnap off teporarily
                bool OSnapWasOn = ModelAidSettings.Osnap;
                if (OSnapWasOn) ModelAidSettings.Osnap = false;

                //constraining the user to select a point along the z-axis.
                ptSelect.Constrain(new Line(finalTerrain.basePoint, finalTerrain.basePoint+Vector3d.ZAxis));
                ptSelect.SetCommandPrompt("Adjust the height");
                //setting the base point for this GetPoint method. and the second parameter 'true'
                //makes the distance to the user current point to the base point visible in the status bar
                ptSelect.SetBasePoint(finalTerrain.basePoint, true);
                //writing code that will be executed dynamically as the user is moving his mouse before selecting
                ptSelect.DynamicDraw += (sender, e) =>
                {
                    //drawing a dynamic line from the base point to the current point
                    e.Display.DrawLine(finalTerrain.basePoint, e.CurrentPoint, System.Drawing.Color.LightGray);
                    //calculating the target height of the terrain as wanted by the user
                    double tempHeight = e.CurrentPoint.DistanceTo(finalTerrain.basePoint);
                    //calculating the scale factor
                    double scaleFactor = tempHeight / refHeight;
                    //now scaling the created temporary terrain along z-axis to match the target height
                    tempTerrain.Scale1D(finalTerrain.basePoint, Vector3d.ZAxis, scaleFactor);
                    //now displaying scaled surface as preview to the user
                    e.Display.DrawSurface(tempTerrain.terrainSurface, System.Drawing.Color.LightGray, 5);
                    //scaling the temporary surface back down to its original height so that these
                    //temporary changes dont all get compounded
                    tempTerrain.Scale1D(finalTerrain.basePoint, Vector3d.ZAxis, 1/scaleFactor);
                };

                //if the user selection is not a point
                if(ptSelect.Get() != GetResult.Point)
                {
                    //displaying a message and restoring the original surface that was deleted
                    RhinoApp.WriteLine("Invalid height selected");
                    doc.Objects.AddSurface(finalTerrain.originalSurface);
                    //exiting the function
                    return Result.Failure;
                }

                //restoring the OSnap setting to its original state
                if (OSnapWasOn) ModelAidSettings.Osnap = true;
                heightPoint = ptSelect.Point();
            }

            //setting the terrainHeight to the height that was selected by the user
            finalTerrain.terrainHeight = heightPoint.DistanceTo(finalTerrain.basePoint);
            //making the terrain undulations
            finalTerrain.makeTerrain(generationCount);
            //rendering the terrain in the document
            finalTerrain.render();

            //this is the temporary surface that is corresponding to the water level
            NurbsSurface tempSurface = finalTerrain.originalSurface;
            //this array will contain the final water surfaces after the intersection
            Brep[] waterFaces;
            //we are now taking the water level (height information) from the user
            using (GetPoint heightSelect = new GetPoint())
            {
                //constraining the user motion to along z-axis
                heightSelect.Constrain(new Line(finalTerrain.basePoint, finalTerrain.basePoint+Vector3d.ZAxis));
                heightSelect.SetCommandPrompt("Select Water level");
                //setting the basepoint of the terrain to the base Point of the terrain
                heightSelect.SetBasePoint(finalTerrain.basePoint, true);
                //defining code to be executed dynamically while selecting the point
                heightSelect.DynamicDraw += (sender, e) =>
                {
                    e.Display.DrawLine(finalTerrain.basePoint, e.CurrentPoint, System.Drawing.Color.LightGray);
                    double waterHeight = e.CurrentPoint.DistanceTo(finalTerrain.basePoint);
                    //calculting the translation vector
                    //a division by 3 allows the user for moe precise control
                    Vector3d translation = (e.CurrentPoint - finalTerrain.basePoint)/3;
                    //correcting the translation vector so that the water level is always below the neutral terrain level
                    if(translation.Z > 0)translation = -translation;
                    tempSurface.Translate(translation);
                    //these arrays will later contain the intersection data of the water level plane and the terrain
                    Curve[] edgeCurves;
                    Point3d[] intersectionPts;

                    //we are not intersecting the water level plane and the terrain and storing the data in the above arrays
                    Intersection.SurfaceSurface(tempSurface, finalTerrain.terrainSurface, 0, out edgeCurves, out intersectionPts);
                    //now creating the water faces out of the intersection curves
                    Brep[] waterSurfaces = Brep.CreatePlanarBreps(new Rhino.Collections.CurveList(edgeCurves));
                    //now dynamically rendering the water faces as preview for the user.
                    if(waterSurfaces != null)
                    {
                        foreach (Brep srf in waterSurfaces)
                        {
                            if (srf != null)
                            {
                                e.Display.DrawBrepShaded(srf, new Rhino.Display.DisplayMaterial(System.Drawing.Color.Blue));
                            }
                            else
                            {
                                Debug.WriteLine("the brep was a null !!");
                            }
                        }
                    }

                    tempSurface.Translate(-translation);
                };
                //if the user selection is invalid then
                if (heightSelect.Get() != GetResult.Point)
                {
                    //dispaying the error message and exiting the function
                    RhinoApp.WriteLine("Invalid water level");
                    return Result.Failure;
                }
                //storing the user selected point in this variable
                Point3d waterPt = heightSelect.Point();
                //calculating the waterlevel. Dividing by 3 to allow a precise height control for the user
                double waterLevel = waterPt.DistanceTo(finalTerrain.basePoint) / 3;
                //translation vector to move the water level plane
                Vector3d trans = (waterPt - finalTerrain.basePoint)/3;
                //making sure the water level is beloow the neutral terrain level by correcting the translation vector
                if (trans.Z > 0) trans = -trans;
                //moving the water level plane by the translation vector
                tempSurface.Translate(trans);
                //the empty arrays which will eventually have the intersection data of the water plane and the terrain
                Curve[] waterEdges;
                Point3d[] intersectionPoints;
                //intersecting the water level plane and the terrain and storing the data in the above arrays
                Intersection.SurfaceSurface(finalTerrain.terrainSurface, tempSurface, 0, out waterEdges, out intersectionPoints);
                //creating planar breps, representing water surfaces, out of the intersection curves.
                waterFaces = Brep.CreatePlanarBreps(new Rhino.Collections.CurveList(waterEdges));
            }
            //now rendering the water faces in the document
            if (waterFaces != null)
            {
                foreach (Brep water in waterFaces)
                {
                    if (water != null)
                    {
                        Guid waterGuid = doc.Objects.AddBrep(water);
                        Rhino.DocObjects.ObjRef waterRef = new Rhino.DocObjects.ObjRef(waterGuid);
                        Rhino.DocObjects.RhinoObject waterObj = waterRef.Object();
                        waterObj.Attributes.ObjectColor = System.Drawing.Color.Blue;
                        waterObj.Attributes.ColorSource = Rhino.DocObjects.ObjectColorSource.ColorFromObject;
                        waterObj.CommitChanges();
                    }
                    else
                    {
                        Debug.WriteLine("brep is a null");
                    }
                }
            }else
            {
                Debug.WriteLine("brep is a null");
            }
            //redrawing the views
            doc.Views.Redraw();
            //finished the command and exiting the method
            return Result.Success;
        }
    }
}