namespace Backend.Models.Dto;

public class GraphNodeUpdateDto
{
    public string? Kind { get; set; }

    public string? Title { get; set; }

    public string? BodyText { get; set; }

    public decimal? PriorOdds { get; set; }
    public decimal? PosteriorOdds { get; set; }
}
