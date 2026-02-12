using backend.Models;
using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookStoreApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BooksController : ControllerBase
{
    private readonly BooksService _booksService;

    public BooksController(BooksService booksService) =>
        _booksService = booksService;

    [HttpGet]
    public async Task<List<Book>> Get() =>
        await _booksService.GetAsync();

    [HttpGet("{id:length(24)}")]
    public async Task<ActionResult<Book>> Get(string id)
    {
        var book = await _booksService.GetAsync(id);

        if (book is null)
        {
            return NotFound();
        }

        return book;
    }

    [HttpPost]
    public async Task<IActionResult> Post(Book newBook)
    {
        await _booksService.CreateAsync(newBook);

        return CreatedAtAction(nameof(Get), new { id = newBook.Id }, newBook);
    }

    [HttpPut("{id:length(24)}")]
    public async Task<IActionResult> Update(string id, Book updatedBook)
    {
        var book = await _booksService.GetAsync(id);

        if (book is null)
        {
            return NotFound();
        }

        updatedBook.Id = book.Id;

        await _booksService.UpdateAsync(id, updatedBook);

        return NoContent();
    }

    [HttpPatch("{id:length(24)}")]
    public async Task<IActionResult> Patch(string id, [FromBody] BookPartialUpdateDto bookPartialUpdateDto)
    {
        // Validate the input
        if (bookPartialUpdateDto == null) 
            return BadRequest("Invalid book data.");

        if (bookPartialUpdateDto.Price is < 0) 
            return BadRequest("Price must be non-negative.");

        // Nothing provided to update
        if (bookPartialUpdateDto.BookName is null && !bookPartialUpdateDto.Price.HasValue && bookPartialUpdateDto.Category is null && bookPartialUpdateDto.Author is null)
            return BadRequest("No fields provided to update.");

        var exists = await _booksService.GetAsync(id);
        if (exists is null) return NotFound();

        var ok = await _booksService.PatchAsync(id, bookPartialUpdateDto);
        if (!ok) return NotFound(); // matched 0 (shouldn’t happen because we checked)

        return NoContent();

    }

    [HttpDelete("{id:length(24)}")]
    public async Task<IActionResult> Delete(string id)
    {
        var book = await _booksService.GetAsync(id);

        if (book is null)
        {
            return NotFound();
        }

        await _booksService.RemoveAsync(id);

        return NoContent();
    }
}