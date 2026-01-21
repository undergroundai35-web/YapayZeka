using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace UniCP.Models.MsK;

[Keyless]
[Table("VIEW_ORTAK_PROJE_ISIMLERI")]
public partial class VIEW_ORTAK_PROJE_ISIMLERI
{
    public int LNGKOD { get; set; }

    [StringLength(200)]
    public string TXTORTAKPROJEADI { get; set; }
}
