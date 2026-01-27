using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniCP.Models.MsK
{
    [Table("TBL_KULLANICI_FIRMA")]
    public class TBL_KULLANICI_FIRMA
    {
        [Key]
        public int LNGKOD { get; set; }

        public int LNGKULLANICIKOD { get; set; }

        public int LNGFIRMAKOD { get; set; }
    }
}
