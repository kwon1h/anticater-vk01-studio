using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
internal static class BuildAssets
{
    private static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath(); p.AddArc(x,y,r*2,r*2,180,90); p.AddArc(x+w-r*2,y,r*2,r*2,270,90); p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90); p.AddArc(x,y+h-r*2,r*2,r*2,90,90); p.CloseFigure(); return p;
    }
    private static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(size/256f,size/256f);
            using(var shape=Rounded(8,8,240,240,56)) using(var gradient=new LinearGradientBrush(new Point(8,8),new Point(248,248),Color.FromArgb(39,34,61),Color.FromArgb(19,25,37))) using(var line=new Pen(Color.FromArgb(72,65,95),2)) {g.FillPath(gradient,shape);g.DrawPath(line,shape);}
            using(var fill=new SolidBrush(Color.FromArgb(25,30,45))) g.FillEllipse(fill,52,52,152,152);
            using(var gradient=new LinearGradientBrush(new Point(52,52),new Point(204,204),Color.FromArgb(197,181,255),Color.FromArgb(143,120,231))) using(var pen=new Pen(gradient,13))g.DrawEllipse(pen,52,52,152,152);
            using(var fill=new SolidBrush(Color.FromArgb(36,42,60)))g.FillEllipse(fill,75,75,106,106);
            using(var pen=new Pen(Color.FromArgb(116,227,195),13)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,128,84,128,108);}
            using(var fill=new SolidBrush(Color.FromArgb(184,164,255)))g.FillEllipse(fill,121,121,14,14);
            using(var pen=new Pen(Color.FromArgb(105,114,140),5)){pen.StartCap=pen.EndCap=LineCap.Round;g.DrawLine(pen,128,28,128,38);g.DrawLine(pen,28,128,38,128);g.DrawLine(pen,218,128,228,128);g.DrawLine(pen,128,218,128,228);}
        }
        return bitmap;
    }
    private static void Main(string[] args)
    {
        string root=args[0];Directory.CreateDirectory(root);var sizes=new[]{16,24,32,48,64,128,256};var bytes=new List<byte[]>();
        foreach(int size in sizes)using(var b=Render(size))using(var stream=new MemoryStream()){b.Save(stream,ImageFormat.Png);bytes.Add(stream.ToArray());if(size==256)b.Save(Path.Combine(root,"icon.png"),ImageFormat.Png);}
        using(var file=File.Create(Path.Combine(root,"app.ico")))using(var writer=new BinaryWriter(file))
        {writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(bytes[i].Length);writer.Write(offset);offset+=bytes[i].Length;}foreach(var data in bytes)writer.Write(data);}
    }
}
