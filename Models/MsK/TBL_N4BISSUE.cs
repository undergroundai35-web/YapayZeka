using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniCP.Models.MsK;

[Table("TBL_N4BISSUE")]
public partial class TBL_N4BISSUE
{
    [Key]
    public int LNGKOD { get; set; }

    public string? TXTTICKETNO { get; set; }

    public virtual ICollection<TBL_N4BISSSEFILE> TBL_N4BISSSEFILEs { get; set; } = new List<TBL_N4BISSSEFILE>();
}
