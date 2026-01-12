using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace UniCP.Models.MsK;

[Table("TBL_TALEP")]
public partial class TBL_TALEP
{
    [Key]
    public int LNGKOD { get; set; }

    public int? LNGPROJEKOD { get; set; }

    [StringLength(512)]
    [Unicode(false)]
    public string? TXTTALEPBASLIK { get; set; }

    [Unicode(false)]
    public string? TXTTALEPACIKLAMA { get; set; }

    public int? LNGTFSNO { get; set; }

    public int? LNGVARUNAKOD { get; set; }

    [StringLength(10)]
    public string? BYTDURUM { get; set; }

    [InverseProperty("LNGTALEPKODNavigation")]
    public virtual ICollection<TBL_TALEP_FILE> TBL_TALEP_FILEs { get; set; } = new List<TBL_TALEP_FILE>();

    [InverseProperty("LNGTALEPKODNavigation")]
    public virtual ICollection<TBL_TALEP_NOTLAR> TBL_TALEP_NOTLARs { get; set; } = new List<TBL_TALEP_NOTLAR>();
}
