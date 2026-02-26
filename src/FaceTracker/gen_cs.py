header = """namespace FaceFinderDemo.FaceDetection;

public static class FaceMeshConnections
{
    public const int IrisCenterLeft = 473;
    public const int IrisCenterRight = 468;

"""
footer = """
}
"""
body = open("connections.txt", "r").read()
open("FaceDetection/FaceMeshConnections.cs", "w").write(header + body + footer)
print("Done")
