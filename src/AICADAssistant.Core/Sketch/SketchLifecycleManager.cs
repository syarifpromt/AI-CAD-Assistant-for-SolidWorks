using System;
using AICADAssistant.Core.Models.Contracts;

namespace AICADAssistant.Core.Sketch
{
    public enum SketchState
    {
        Idle = 0,
        SketchStarted = 1,
        GeometryCreated = 2,
        GeometryValidated = 3,
        SketchClosed = 4,
        ProfileSelected = 5,
        FeatureExecuted = 6,
        Error = 7
    }

    public class SketchLifecycleManager
    {
        public SketchState CurrentState { get; private set; }
        public string ActiveSketchName { get; private set; }
        public string TargetPlaneOrFace { get; private set; }
        public int SegmentCount { get; private set; }
        public string LastErrorMessage { get; private set; }

        public SketchLifecycleManager()
        {
            Reset();
        }

        public void Reset()
        {
            CurrentState = SketchState.Idle;
            ActiveSketchName = null;
            TargetPlaneOrFace = null;
            SegmentCount = 0;
            LastErrorMessage = null;
        }

        public bool BeginSketch(string planeOrFaceName, out string error)
        {
            error = null;
            if (CurrentState != SketchState.Idle && CurrentState != SketchState.FeatureExecuted && CurrentState != SketchState.Error)
            {
                error = string.Format("Cannot begin sketch from state: {0}. Resetting sketch state.", CurrentState);
                Reset();
            }

            TargetPlaneOrFace = planeOrFaceName ?? "Top Plane";
            CurrentState = SketchState.SketchStarted;
            return true;
        }

        public void RecordGeometry(int count = 1)
        {
            SegmentCount += count;
            if (CurrentState == SketchState.SketchStarted || CurrentState == SketchState.GeometryCreated)
            {
                CurrentState = SketchState.GeometryCreated;
            }
        }

        public bool ValidateGeometry(bool isClosed, double area, out string error)
        {
            error = null;
            if (SegmentCount == 0)
            {
                error = "No sketch segments were created";
                CurrentState = SketchState.Error;
                LastErrorMessage = error;
                return false;
            }

            if (!isClosed)
            {
                error = "Sketch profile is not closed";
                CurrentState = SketchState.Error;
                LastErrorMessage = error;
                return false;
            }

            if (area <= 0.0)
            {
                error = "Sketch profile has zero or negative area";
                CurrentState = SketchState.Error;
                LastErrorMessage = error;
                return false;
            }

            CurrentState = SketchState.GeometryValidated;
            return true;
        }

        public void MarkSketchClosed(string sketchFeatureName = null)
        {
            ActiveSketchName = sketchFeatureName;
            CurrentState = SketchState.SketchClosed;
        }

        public void MarkProfileSelected(string sketchFeatureName = null)
        {
            if (!string.IsNullOrEmpty(sketchFeatureName))
            {
                ActiveSketchName = sketchFeatureName;
            }
            CurrentState = SketchState.ProfileSelected;
        }

        public void MarkFeatureExecuted()
        {
            CurrentState = SketchState.FeatureExecuted;
        }

        public void Fail(string errorMessage)
        {
            CurrentState = SketchState.Error;
            LastErrorMessage = errorMessage;
        }
    }
}

