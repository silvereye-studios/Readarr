using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.OPDS
{
    [V1ApiController]
    public class OPDSController : Controller
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IContentTypeProvider _mimeTypeProvider;
        private readonly IMapCoversToLocal _coverMapper;

        public OPDSController(IAuthorService authorService,
                          IBookService bookService,
                          IEditionService editionService,
                          IMapCoversToLocal coverMapper,
                          IMediaFileService mediaFileService)
        {
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _mimeTypeProvider = new FileExtensionContentTypeProvider();
            _coverMapper = coverMapper;
            _mediaFileService = mediaFileService;
        }

        // /opds
        [HttpGet]
        public OPDSCatalogResource GetOPDSCatalog()
        {
            var catalog = OPDSResourceMapper.ToOPDSCatalogResource();
            return catalog;
        }

        protected bool IsDigitsOnly(string str)
        {
            foreach (var c in str)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        // /opds/publications/search
        [HttpGet("search")]
        [HttpGet("publications/search")]
        public OPDSPublicationsResource GetPublicationsForQuery([FromQuery] PagingRequestResource paging, [FromQuery] string query, [FromQuery] string title, [FromQuery] string author)
        {
            var baseUrl = string.Format("{0}://{1}", Request.Scheme, Request.Host);
            var pagingResource = new PagingResource<OPDSPublicationResource>(paging);
            var pagingSpec = new PagingSpec<Book>
            {
                Page = pagingResource.Page,
                PageSize = pagingResource.PageSize,
                SortKey = pagingResource.SortKey,
                SortDirection = pagingResource.SortDirection
            };

            if (query.IsNotNullOrWhiteSpace())
            {
                query = query.ToLower();
                pagingSpec.FilterExpressions.Add(v => v.Title.Contains(query) || v.Author.Value.CleanName.Contains(query));
            }
            else if (title.IsNotNullOrWhiteSpace() && author.IsNotNullOrWhiteSpace())
            {
                title = title.ToLower();
                author = author.ToLower();
                pagingSpec.FilterExpressions.Add(v => v.Title.Contains(title) && v.Author.Value.CleanName.Contains(author));
            }
            else if (title.IsNotNullOrWhiteSpace())
            {
                title = title.ToLower();
                pagingSpec.FilterExpressions.Add(v => v.Title.Contains(title));
            }
            else if (author.IsNotNullOrWhiteSpace())
            {
                author = author.ToLower();
                pagingSpec.FilterExpressions.Add(v => v.Author.Value.CleanName.Contains(author));
            }
            else
            {
                throw new BadRequestException("No search term specified in query");
            }

            pagingSpec = _bookService.BooksWithFiles(pagingSpec);

            var publications = OPDSResourceMapper.ToOPDSPublicationsResource(pagingSpec.Page, pagingSpec.PageSize, pagingSpec.TotalRecords);
            publications.Publications = MapToResource(baseUrl, pagingSpec.Records);

            return publications;
        }

        // /opds/publications
        [HttpGet("publications")]
        public OPDSPublicationsResource GetOPDSPublications([FromQuery] PagingRequestResource paging)
        {
            var baseUrl = string.Format("{0}://{1}", Request.Scheme, Request.Host);

            var pagingResource = new PagingResource<OPDSPublicationResource>(paging);
            var pagingSpec = new PagingSpec<Book>
            {
                Page = pagingResource.Page,
                PageSize = pagingResource.PageSize,
                SortKey = pagingResource.SortKey,
                SortDirection = pagingResource.SortDirection
            };
            pagingSpec = _bookService.BooksWithFiles(pagingSpec);

            var publications = OPDSResourceMapper.ToOPDSPublicationsResource(pagingSpec.Page, pagingSpec.PageSize, pagingSpec.TotalRecords);
            publications.Publications = MapToResource(baseUrl, pagingSpec.Records);

            return publications;
        }

        // /opds/publications/{int:id}
        [HttpGet("publications/{id:int}")]
        public OPDSPublicationResource GetOPDSPublication(int id)
        {
            var baseUrl = string.Format("{0}://{1}", Request.Scheme, Request.Host);
            var images = new List<MediaCover>();
            var metadataTask = Task.Run(() => _authorService.GetAllAuthors());
            var book = _bookService.GetBook(id);
            var author = _authorService.GetAuthor(book.AuthorId);
            var bookfiles = _mediaFileService.GetFilesByBook(book.Id);

            if (!bookfiles.Any())
            {
                throw new BadRequestException("No book files exist for the given book id");
            }

            var ebookEdition = book.Editions?.Value.Where(x => x.IsEbook).SingleOrDefault();
            var selectedEdition = book.Editions?.Value.Where(x => x.Monitored).SingleOrDefault();
            var covers = selectedEdition?.Images ?? new List<MediaCover>();
            _coverMapper.ConvertToLocalUrls(book.Id, MediaCoverEntity.Book, covers);
            _coverMapper.ConvertToLocalUrls(book.Id, MediaCoverEntity.Book, images);
            book.Author = author;

            return OPDSResourceMapper.ToOPDSPublicationResource(baseUrl, book, bookfiles, ebookEdition, images);
        }

        protected List<OPDSPublicationResource> MapToResource(string baseUrl, List<Book> books)
        {
            var publications = new List<OPDSPublicationResource>();
            var metadataTask = Task.Run(() => _authorService.GetAllAuthors());
            for (var i = 0; i < books.Count; i++)
            {
                var images = new List<MediaCover>();
                var book = books[i];
                var bookfiles = _mediaFileService.GetFilesByBook(book.Id);
                var selectedEdition = book.Editions?.Value.Where(x => x.Monitored).SingleOrDefault();
                var ebookEdition = book.Editions?.Value.Where(x => x.IsEbook).FirstOrDefault();
                var anyEdition = book.Editions?.Value.FirstOrDefault();
                var covers = selectedEdition?.Images ?? new List<MediaCover>();
                _coverMapper.ConvertToLocalUrls(book.Id, MediaCoverEntity.Book, covers);
                var publication = OPDSResourceMapper.ToOPDSPublicationResource(baseUrl, book, bookfiles, ebookEdition ?? selectedEdition ?? anyEdition, covers);
                publications.Add(publication);
            }

            return publications;
        }
    }
}
