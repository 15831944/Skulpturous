﻿using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.GraphicsInterface;
using Microsoft.Kinect;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System;


#pragma warning disable 1591

namespace KinectSamples
{
    public class KinectrotateCylinderJig : KinectPointCloudJig
    {
        // Our transient solids (cursor sphere & tube) are yellow

        const short transSolColor = 4;

        // Our final solids will be green

        const short finalSolColor = 4;

        // A transaction and database to add solids

        private Transaction _tr;
        private Document _doc;
        private int ct;
        // A list of vertices to draw between
        // (we use this for the final polyline creation)

        private Point3dCollection _vertices;
        private int _lastDrawnVertex;

        // Entities to create our solid

        private DBObjectCollection _created;

        // The radius of the profile circle to create

        private double _profSide;
        private int _firstdraw;
        private bool _isRotate;
        private bool _changeaxis;
        // The location at which to draw a sphere when resizing

        private Point3d _resizeLocation;
        private Point3d leftHand;
        private Point3d rightHand;
        private Point3d leftHip;
        private Vector3d vRot;

        // The approximate length of each swept segment
        // (as a multiple of the radius)

        private double _segFactor;
        private Solid3d cube;
        // Flags to indicate Kinect gesture modes

        private bool _resizing;     // Drawing mode active
        private bool _drawing;     // Drawing mode active
        private int _resizebool;

        public KinectrotateCylinderJig(
          Document doc, Transaction tr, double profSide, double factor
        )
        {
            // Initialise the various members

            _doc = doc;
            _tr = tr;
            _vertices = new Point3dCollection();
            _lastDrawnVertex = -1;
            _resizing = false;
            _resizebool = 0;
            _drawing = false;
            leftHand = new Point3d();
            rightHand = new Point3d();
            leftHip = new Point3d();
            _isRotate = false;
            vRot = new Vector3d();
            _changeaxis = false;
            _firstdraw = 0;
            ct = 0;
            _created = new DBObjectCollection();
            _profSide = profSide;
            _segFactor = factor;

            cube = new Solid3d();
            cube.CreateFrustum(0.5, 0.5, 0.8,0.5);

            Words.Add("red");
            Words.Add("green");
            Words.Add("blue");
            Words.Add("yellow");
            Words.Add("pink");
            Words.Add("magenta");
            Words.Add("cyan");
        }

        protected override void OnFoundWord(WordFoundEventArgs e)
        {
            base.OnFoundWord(e);

            switch (e.Word)
            {
                case "RED":
                    ColorIndex = 1;
                    break;
                case "YELLOW":
                    ColorIndex = 2;
                    break;
                case "GREEN":
                    ColorIndex = 3;
                    break;
                case "CYAN":
                    ColorIndex = 4;
                    break;
                case "BLUE":
                    ColorIndex = 5;
                    break;
                case "PINK":
                case "MAGENTA":
                    ColorIndex = 6;
                    break;
                default:
                    break;
            }
        }

        public override void OnSkeletonFrameReady(
          object sender, SkeletonFrameReadyEventArgs e
        )
        {
            Point3d pt = new Point3d();
            if (!Finished)
            {
                using (SkeletonFrame s = e.OpenSkeletonFrame())
                {
                    if (s != null)
                    {
                        Skeleton[] skels = new Skeleton[s.SkeletonArrayLength];
                        s.CopySkeletonDataTo(skels);

                        foreach (Skeleton data in skels)
                        {
                            if (
                              data.TrackingState == SkeletonTrackingState.Tracked
                            )
                            {
                                leftHip =
                                  PointFromVector(
                                    data.Joints[JointType.HipLeft].Position, false
                                  );
                                leftHand =
                                  PointFromVector(
                                    data.Joints[JointType.HandLeft].Position, false
                                  );
                                Point3d rightHand =
                                  PointFromVector(
                                    data.Joints[JointType.HandRight].Position, false
                                  );

                                if (
                                  leftHand.DistanceTo(Point3d.Origin) > 0 &&
                                  rightHand.DistanceTo(Point3d.Origin) > 0 &&
                                  leftHand.DistanceTo(rightHand) < 0.03)
                                {
                                    // Hands are less than 3cm from each other

                                    _drawing = false;
                                    _resizing = false;
                                    _isRotate = false;
                                    _changeaxis = false;
                                    _resizebool = 0;
                                    Finished = true;
                                }
                                else
                                {
                                    // Hands are within 10cm of each other vertically
                                    // and both hands are above the waist, so we resize
                                    // the profile radius


                                    _resizing = (leftHand.Z > leftHip.Z &&
                                       rightHand.Z > leftHip.Z &&
                                       Math.Abs(leftHand.Z - rightHand.Z) < 0.5);

                                    _changeaxis = (leftHand.Z > leftHip.Z &&
                                           rightHand.Z > leftHip.Z &&
                                           Math.Abs(leftHand.Z - rightHand.Z) < 0.5);



                                    // If the left hand is below the waist, we draw
                                    _isRotate = (leftHand.Z < leftHip.Z);
                                    _drawing = false;
                                }

                                //_resizing = true;

                                if (ct == 0)
                                {
                                    pt = rightHand;
                                    ct = 1;
                                }
                                if (_resizing)
                                {
                                    // If resizing, set some data to help draw
                                    // a sphere where we're resizing

                                    Vector3d vec = (leftHand - rightHand) / 2;
                                    _resizeLocation = pt + vec;
                                    _profSide = vec.Length / (Math.Sqrt(3));
                                    vRot = rightHand.GetVectorTo(leftHand); //new Point3d(-3, 4, 0).GetVectorTo(new Point3d(-3, -4, 0)); //rightHand.GetVectorTo(leftHand);
                                }

                                //       if (_changeaxis)
                                //     {
                                //        vRot = rightHand.GetVectorTo(leftHand);
                                //  }

                                //if (_isRotate)
                                // {
                                //     cube.WorldDraw(draw);
                                // }

                                if (_drawing)
                                {
                                    // If we have at least one prior vertex...

                                    if (_vertices.Count > 0)
                                    {
                                        // ... check whether we're a certain distance
                                        // away from the last one before adding it (this
                                        // smooths off the jitters of adding every point)

                                        Point3d lastVert =
                                          _vertices[_vertices.Count - 1];
                                        if (
                                          lastVert.DistanceTo(rightHand) > _profSide * 4
                                        )
                                        {
                                            // Add the new vertex to our list

                                            _vertices.Add(rightHand);
                                        }
                                    }
                                    else
                                    {
                                        // Add the first vertex to our list

                                        _vertices.Add(rightHand);
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }

        public void Cleanup()
        {
            _vertices.Clear();

            foreach (DBObject obj in _created)
            {
                obj.Dispose();
            }
            _created.Clear();
        }

        private bool GenerateTube(
          double profRad, Point3dCollection pts, out Solid3d sol
        )
        {
            bool readyToBreak;

            // Let's start by creating our spline path

            using (Spline path = new Spline(pts, 0, 0.0))
            {
                double pathLen = path.GetDistanceAtParameter(path.EndParam);
                readyToBreak = (pathLen > _profSide * _segFactor);

                // And our sweep profile

                Circle profile =
                  new Circle(pts[0], pts[1] - pts[0], profRad);
                using (profile)
                {
                    // Then our sweep options

                    SweepOptionsBuilder sob = new SweepOptionsBuilder();

                    // Align the entity to sweep to the path

                    sob.Align =
                      SweepOptionsAlignOption.AlignSweepEntityToPath;

                    // The base point is the start of the path

                    sob.BasePoint = path.StartPoint;

                    // The profile will rotate to follow the path

                    sob.Bank = true;
                    using (SweepOptions sweepOpts = sob.ToSweepOptions())
                    {
                        sol = new Solid3d();
                        sol.ColorIndex = ColorIndex;

                        // Sweep our profile along our path

                        sol.CreateSweptSolid(profile, path, sweepOpts);
                    }
                }
            }
            _lastDrawnVertex = pts.Count - 1;

            return readyToBreak;
        }

        protected override SamplerStatus SamplerData()
        {
            // If not finished, but stopped drawing, add the
            // geometry that was previously drawn to the database

            if (!_drawing &&
                  (_created.Count > 0 || _vertices.Count > 0)
              )
            {
                AddSolidOrPath();
            }

            return base.SamplerData();
        }

        // Helper functions to extract/blank portions of our
        // vertex list (when we want to draw the beginning of it)

        private void ClearAllButLast(Point3dCollection pts, int n)
        {
            while (pts.Count > n)
            {
                pts.RemoveAt(0);
            }
            _lastDrawnVertex = -1;
        }

        private Point3dCollection GetAllButLast(
          Point3dCollection pts, int n
        )
        {
            Point3dCollection res = new Point3dCollection();
            for (int i = 0; i < pts.Count - n; i++)
            {
                res.Add(pts[i]);
            }
            return res;
        }

        protected override bool WorldDrawData(WorldDraw draw)
        {
            if (!base.WorldDrawData(draw))
                return false;

            short origCol = draw.SubEntityTraits.Color;

            if (_resizing)
            {
                //  using (Solid3d cube = new Solid3d())
                //{
                try
                {


                    //cube.CreateBox(0.5, 0.5, 0.5);
                    _firstdraw = _firstdraw + 1;


                    if (cube != null)
                    {
                        //int i = 20000;
                        bool chk = false;
                        //cube.TransformBy(
                        //  Matrix3d.Displacement(
                        //     _resizeLocation - Point3d.Origin
                        //  )
                        //  );

                        // Draw the cursor

                        draw.SubEntityTraits.Color = ColorIndex;
                        cube.WorldDraw(draw);
                        vRot = leftHand.GetVectorTo(rightHand); ;// new Point3d(-3, 4, 0).GetVectorTo(new Point3d(-3, -4, 0)); //rightHand.GetVectorTo(leftHand);

                        System.Threading.Thread.Sleep((int)System.TimeSpan.FromSeconds(0.2).TotalMilliseconds);

                        //chk = (leftHand.Z < leftHip.Z);

                        //while (true)
                        //{
                        //  if (chk)
                        //    break;
                        //Point3d pt1 = rightHand - leftHand;
                        cube.TransformBy(Matrix3d.Rotation(0.1, vRot, Point3d.Origin));
                        //}

                        /*if (chk)
                        {
                            cube.TransformBy(Matrix3d.Rotation(1.046, vRot, leftHand));
                            System.Threading.Thread.Sleep((int)System.TimeSpan.FromSeconds(2).TotalMilliseconds);
                        }  */

                        //cube.TransformBy(Matrix3d.Rotation(0.5236, vRot, leftHand));
                    }

                    // if(_changeaxis)

                    //            vRot = rightHand.GetVectorTo(leftHand);
                    //          cube.TransformBy(Matrix3d.Rotation(0.5236,vRot,leftHand));

                    //   if (leftHand.Z > leftHip.Z)
                    //     _isRotate = false;

                }
                catch (System.Exception ex)
                {

                    _doc.Editor.WriteMessage(
                      "\nException: {0} - {1}", ex.Message, ex.InnerException
                    );
                }
                finally
                {
                    draw.SubEntityTraits.Color = origCol;
                }
                // }
                return true;
            }

            if (_isRotate)
            {
                cube.WorldDraw(draw);
            }

            // If we're currently drawing...

            if (_drawing)
            {
                Solid3d sol = null;
                try
                {
                    // If we have vertices that haven't yet been drawn...

                    if (_vertices.Count > 1 //&&
                        //_vertices.Count - 1 > _lastDrawnVertex
                      )
                    {
                        // ... generate a tube

                        if (GenerateTube(_profSide, _vertices, out sol))
                        {
                            // We now need to break the pipe...

                            // If it was created, add it to our list to draw

                            _created.Add(sol);
                            sol = null;

                            // Clear all but the last vertex to draw from
                            // next time

                            ClearAllButLast(_vertices, 1);
                        }
                    }
                }
                catch
                {
                    // If the tube generation failed...

                    if (sol != null)
                    {
                        sol.Dispose();
                    }

                    // Loop, creating the most recent successful tube we can

                    bool succeeded = false;
                    int n = 1;

                    do
                    {
                        try
                        {
                            // Generate the previous, working tube using all
                            // but the last points (if it fails, one more is
                            // excluded per iteration, until we get a working
                            // tube)

                            GenerateTube(
                              _profSide, GetAllButLast(_vertices, n++), out sol
                            );

                            _created.Add(sol);
                            sol = null;
                            succeeded = true;
                        }
                        catch { }
                    }
                    while (!succeeded && n < _vertices.Count);

                    if (succeeded)
                    {
                        ClearAllButLast(_vertices, n - 1);

                        if (_vertices.Count > 1)
                        {
                            try
                            {
                                // And generate a tube for the remaining vertices

                                GenerateTube(_profSide, _vertices, out sol);
                            }
                            catch
                            {
                                succeeded = false;
                            }
                        }
                    }

                    if (!succeeded && sol != null)
                    {
                        sol.Dispose();
                        sol = null;
                    }
                }

                // Draw our solid(s)

                draw.SubEntityTraits.Color = ColorIndex;

                foreach (DBObject obj in _created)
                {
                    Entity ent = obj as Entity;
                    if (ent != null)
                    {
                        try
                        {
                            ent.WorldDraw(draw);
                        }
                        catch
                        { }
                    }
                }

                if (sol != null)
                {
                    try
                    {
                        sol.WorldDraw(draw);
                    }
                    catch
                    { }
                }

                if (_vertices.Count > 0)
                {
                    Point3d lastPt = _vertices[_vertices.Count - 1];

                    // Create a cursor sphere

                    using (Solid3d cursor = new Solid3d())
                    {
                        try
                        {
                            cursor.CreateBox(_profSide, _profSide, _profSide);

                            if (cursor != null)
                            {
                                cursor.TransformBy(
                                  Matrix3d.Displacement(lastPt - Point3d.Origin)
                                );

                                // Draw the cursor

                                draw.SubEntityTraits.Color = 4; // ColorIndex;

                                cursor.WorldDraw(draw);
                            }
                        }
                        catch { }
                    }
                }

                if (sol != null)
                {
                    sol.Dispose();
                }
            }

            draw.SubEntityTraits.Color = origCol;

            return true;
        }

        public void AddSolidOrPath()
        {
            Solid3d sol = null;
            try
            {
                GenerateTube(_profSide, _vertices, out sol);
            }
            catch
            {
                if (sol != null)
                {
                    sol.Dispose();
                    sol = null;
                }
            }

            if (_created.Count > 0 || sol != null)
            {
                if (sol != null)
                {
                    _created.Add(sol);
                }

                BlockTableRecord btr =
                  (BlockTableRecord)_tr.GetObject(
                    _doc.Database.CurrentSpaceId,
                    OpenMode.ForWrite
                  );

                foreach (DBObject obj in _created)
                {
                    Entity ent = obj as Entity;
                    if (ent != null)
                    {
                        //ent.ColorIndex = finalSolColor;

                        btr.AppendEntity(ent);
                        _tr.AddNewlyCreatedDBObject(ent, true);
                    }
                }
                _created.Clear();
            }

            Cleanup();

            _vertices.Clear();
        }
    }

    public class KinectCylinderrotateCommands
    {
        [CommandMethod("ADNPLUGINS", "KINrotateCYL", CommandFlags.Modal)]
        public void ImportFromKinect()
        {
            Document doc =
              Autodesk.AutoCAD.ApplicationServices.
                Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            Transaction tr =
              doc.TransactionManager.StartTransaction();

            // Pass in a default radius of 5cm and a segment length
            // of 10 times that

            KinectrotateCylinderJig kj =
              new KinectrotateCylinderJig(doc, tr, 0.1, 10);

            kj.InitializeSpeech();

            if (!kj.StartSensor())
            {
                ed.WriteMessage(
                  "\nUnable to start Kinect sensor - " +
                  "are you sure it's plugged in?"
                );
                tr.Dispose();
                return;
            }

            PromptResult pr = ed.Drag(kj);

            if (pr.Status != PromptStatus.OK && !kj.Finished)
            {
                kj.StopSensor();
                kj.Cleanup();
                tr.Dispose();
                return;
            }

            // Generate a final point cloud with color before stopping
            // the sensor

            kj.UpdatePointCloud();
            kj.StopSensor();

            kj.AddSolidOrPath();
            tr.Commit();

            // Manually dispose to avoid scoping issues with
            // other variables

            tr.Dispose();

            kj.WriteAndImportPointCloud(doc, kj.Vectors);
        }
    }
}