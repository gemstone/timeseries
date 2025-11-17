// ReSharper disable CheckNamespace
#pragma warning disable 1591

using System;
using System.ComponentModel.DataAnnotations;
using Gemstone.ComponentModel.DataAnnotations;
using Gemstone.Data.Model;

namespace Gemstone.Timeseries.Model;
public class PhasorValues
{
    public string AlternateLabel
    {
        get;
        set;
    }
    public string AlternateLabel2
    {
        get;
        set;
    }
    public string AlternateLabel3
    {
        get;
        set;
    }
    public string Device
    {
        get;
        set;
    }
    public string Label
    {
        get;
        set;
    }
    public int? DeviceID
    {
        get;
        set;
    }

    [Label("Magnitude Tag Name")]
    [Required]
    [StringLength(200)]
    public string MagnitudePointTag
    {
        get;
        set;
    }

    [Label("Angle Tag Name")]
    [Required]
    [StringLength(200)]
    public string AnglePointTag
    {
        get;
        set;
    }

    [Label("Tag Name")]
    [Required]
    [StringLength(200)]
    public string PointTag
    {
        get;
        set;
    }

    public int? PrimaryVoltagePhasorID 
    { 
        get;
        set; 
    }

    public int? SecondaryVoltagePhasorID 
    { 
        get;
        set;
    }

    public string MagnitudeID
    {
        get;
        set;
    }

    public string AngleID
    {
        get;
        set;
    }

    [Label("Phasor ID")]
    public int? PhasorID
    {
        get;
        set;
    }

    [PrimaryKey(true)]
    [Label("Unique Angle Signal ID")]
    public Guid? AngleSignalID
    {
        get;
        set;
    }

    [PrimaryKey(true)]
    [Label("Unique Magnitude Signal ID")]
    public Guid? MagnitudeSignalID
    {
        get;
        set;
    }

    [Label("Angle Signal Reference")]
    [Required]
    [StringLength(200)]
    public string AngleSignalReference
    {
        get;
        set;
    }

    [Label("Magnitude Signal Reference")]
    [Required]
    [StringLength(200)]
    public string MagnitudeSignalReference
    {
        get;
        set;
    }

    [Label("Phasor Type")]
    public char? Type
    {
        get;
        set;
    }
    public int SourceIndex
    {
        get;
        set;
    }

    public char? Phase
    {
        get;
        set;
    }

    public string Company
    {
        get;
        set;
    }

    public decimal Longitude
    {
        get;
        set;
    }

    public decimal Latitude
    {
        get;
        set;
    }

    public DateTime UpdatedOn
    {
        get;
        set;
    }

    public string ID
    {
        get;
        set;
    }

    [PrimaryKey(true)]
    [Label("Unique Signal ID")]
    public Guid? SignalID
    {
        get;
        set;
    }
}
