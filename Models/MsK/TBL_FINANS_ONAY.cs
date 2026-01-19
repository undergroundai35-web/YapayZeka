using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniCP.Models.MsK
{
    [Table("TBL_FINANS_ONAY")]
    public class TBL_FINANS_ONAY
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string OrderId { get; set; }

        [StringLength(100)]
        public string? PONumber { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public int? CreatedBy { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual TBL_KULLANICI? CreatedByUser { get; set; }

        public bool IsRevoked { get; set; } = false;

        public DateTime? RevokedDate { get; set; }

        public int? RevokedBy { get; set; }

        [ForeignKey("RevokedBy")]
        public virtual TBL_KULLANICI? RevokedByUser { get; set; }
    }
}
