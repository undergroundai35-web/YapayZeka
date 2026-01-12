using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniCP.Models.MsK;

[Table("TBL_N4BISSSEFILES")]
public partial class TBL_N4BISSSEFILE
{
    [Key]
    public int LNGKOD { get; set; }

    public int? LNGTBLISSUEKOD { get; set; }

    [ForeignKey("LNGTBLISSUEKOD")]
    public virtual TBL_N4BISSUE? LNGTBLISSUEKODNavigation { get; set; }
}
