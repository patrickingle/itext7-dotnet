/*
This file is part of the iText (R) project.
Copyright (c) 1998-2018 iText Group NV
Authors: iText Software.

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License version 3
as published by the Free Software Foundation with the addition of the
following permission added to Section 15 as permitted in Section 7(a):
FOR ANY PART OF THE COVERED WORK IN WHICH THE COPYRIGHT IS OWNED BY
ITEXT GROUP. ITEXT GROUP DISCLAIMS THE WARRANTY OF NON INFRINGEMENT
OF THIRD PARTY RIGHTS

This program is distributed in the hope that it will be useful, but
WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
or FITNESS FOR A PARTICULAR PURPOSE.
See the GNU Affero General Public License for more details.
You should have received a copy of the GNU Affero General Public License
along with this program; if not, see http://www.gnu.org/licenses or write to
the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
Boston, MA, 02110-1301 USA, or download the license from the following URL:
http://itextpdf.com/terms-of-use/

The interactive user interfaces in modified source and object code versions
of this program must display Appropriate Legal Notices, as required under
Section 5 of the GNU Affero General Public License.

In accordance with Section 7(b) of the GNU Affero General Public License,
a covered work must retain the producer line in every PDF that is created
or manipulated using iText.

You can be released from the requirements of the license by purchasing
a commercial license. Buying such a license is mandatory as soon as you
develop commercial activities involving the iText software without
disclosing the source code of your own applications.
These activities include: offering paid services to customers as an ASP,
serving PDFs on the fly in a web application, shipping iText with a closed
source product.

For more information, please contact iText Software Corp. at this
address: sales@itextpdf.com
*/
using System;
using System.Collections.Generic;
using Common.Logging;
using iText.StyledXmlParser.Css;
using iText.StyledXmlParser.Node;
using iText.Svg;
using iText.Svg.Css;
using iText.Svg.Exceptions;
using iText.Svg.Processors;
using iText.Svg.Renderers;
using iText.Svg.Renderers.Factories;
using iText.Svg.Renderers.Impl;
using iText.Svg.Utils;

namespace iText.Svg.Processors.Impl {
    /// <summary>
    /// Default implementation of
    /// <see cref="iText.Svg.Processors.ISvgProcessor"/>
    /// .
    /// This implementation traverses the
    /// <see cref="iText.StyledXmlParser.Node.INode"/>
    /// tree depth-first,
    /// using a stack to recreate a tree of
    /// <see cref="iText.Svg.Renderers.ISvgNodeRenderer"/>
    /// with the same structure.
    /// </summary>
    public class DefaultSvgProcessor : ISvgProcessor {
        private static readonly ILog LOGGER = LogManager.GetLogger(typeof(iText.Svg.Processors.Impl.DefaultSvgProcessor
            ));

        private ProcessorState processorState;

        private ICssResolver cssResolver;

        private SvgCssContext cssContext;

        private ISvgNodeRendererFactory rendererFactory;

        private ISvgConverterProperties defaultProps;

        private IDictionary<String, ISvgNodeRenderer> namedObjects;

        /// <summary>Instantiates a DefaultSvgProcessor object.</summary>
        public DefaultSvgProcessor() {
        }

        //Processor context
        /// <exception cref="iText.Svg.Exceptions.SvgProcessingException"/>
        public virtual ISvgProcessorResult Process(INode root) {
            return Process(root, new DefaultSvgConverterProperties());
        }

        /// <exception cref="iText.Svg.Exceptions.SvgProcessingException"/>
        public virtual ISvgProcessorResult Process(INode root, ISvgConverterProperties converterProps) {
            if (root == null) {
                throw new SvgProcessingException(SvgLogMessageConstant.INODEROOTISNULL);
            }
            //Setup processorState
            if (converterProps != null) {
                PerformSetup(converterProps);
            }
            else {
                this.defaultProps = new DefaultSvgConverterProperties();
                PerformSetup(this.defaultProps);
            }
            //Find root
            IElementNode svgRoot = FindFirstElement(root, SvgConstants.Tags.SVG);
            if (svgRoot != null) {
                //Iterate over children
                if (converterProps == null) {
                    converterProps = this.defaultProps;
                }
                ExecuteDepthFirstTraversal(svgRoot, converterProps);
                ISvgNodeRenderer rootSvgRenderer = CreateResultAndClean();
                return new DefaultSvgProcessorResult(namedObjects, rootSvgRenderer);
            }
            else {
                throw new SvgProcessingException(SvgLogMessageConstant.NOROOT);
            }
        }

        /// <summary>Load in configuration, set initial processorState and create/fill-in context of the processor</summary>
        /// <param name="converterProps">that contains configuration properties and operations</param>
        private void PerformSetup(ISvgConverterProperties converterProps) {
            processorState = new ProcessorState();
            if (converterProps.GetCssResolver() != null) {
                cssResolver = converterProps.GetCssResolver();
            }
            if (converterProps.GetRendererFactory() != null) {
                rendererFactory = converterProps.GetRendererFactory();
            }
            namedObjects = new Dictionary<String, ISvgNodeRenderer>();
            cssContext = new SvgCssContext();
        }

        //TODO(RND-865): resolve/initialize CSS context
        /// <summary>Start the depth-first traversal of the INode tree, pushing the results on the stack</summary>
        /// <param name="startingNode">node to start on</param>
        private void ExecuteDepthFirstTraversal(INode startingNode, ISvgConverterProperties converterProperties) {
            //Create and push rootNode
            if (startingNode is IElementNode && !rendererFactory.IsTagIgnored((IElementNode)startingNode)) {
                IElementNode rootElementNode = (IElementNode)startingNode;
                ISvgNodeRenderer startingRenderer = rendererFactory.CreateSvgNodeRendererForTag(rootElementNode, null);
                cssResolver.CollectCssDeclarations(startingNode, converterProperties.GetResourceResolver());
                IDictionary<String, String> attributesAndStyles = cssResolver.ResolveStyles(startingNode, cssContext);
                startingRenderer.SetAttributesAndStyles(attributesAndStyles);
                processorState.Push(startingRenderer);
                foreach (INode rootChild in startingNode.ChildNodes()) {
                    Visit(rootChild);
                }
            }
        }

        /// <summary>Extract result from internal processorState and clean up afterwards</summary>
        /// <returns>Root renderer of the processed SVG</returns>
        private ISvgNodeRenderer CreateResultAndClean() {
            return processorState.Pop();
        }

        /// <summary>Recursive visit of the object tree, depth-first, processing the visited node and calling visit on its children.
        ///     </summary>
        /// <remarks>
        /// Recursive visit of the object tree, depth-first, processing the visited node and calling visit on its children.
        /// Visit responsibilities for element nodes:
        /// - Assign styles(CSS & attributes) to element
        /// - Create Renderer based on element
        /// - push & pop renderer to stack
        /// Visit responsibilities for text nodes
        /// - add text to parent object
        /// </remarks>
        /// <param name="node">INode to visit</param>
        private void Visit(INode node) {
            if (node is IElementNode) {
                IElementNode element = (IElementNode)node;
                if (!rendererFactory.IsTagIgnored(element)) {
                    ISvgNodeRenderer renderer = CreateRenderer(element, processorState.Top());
                    if (renderer != null) {
                        renderer.SetAttributesAndStyles(cssResolver.ResolveStyles(node, cssContext));
                        String attribute = renderer.GetAttribute(SvgConstants.Attributes.ID);
                        if (attribute != null) {
                            namedObjects.Put(attribute, renderer);
                        }
                        // this check should be superfluous, but better safe than sorry
                        if (processorState.Top() is IBranchSvgNodeRenderer) {
                            ((IBranchSvgNodeRenderer)processorState.Top()).AddChild(renderer);
                        }
                        processorState.Push(renderer);
                    }
                    foreach (INode childNode in element.ChildNodes()) {
                        Visit(childNode);
                    }
                    if (renderer != null) {
                        processorState.Pop();
                    }
                }
            }
            else {
                if (ProcessAsText(node)) {
                    ProcessText((ITextNode)node);
                }
            }
        }

        /// <summary>Create renderer based on the passed SVG tag and assign its parent</summary>
        /// <param name="tag">SVG tag with all style attributes already assigned</param>
        /// <param name="parent">renderer of the parent tag</param>
        /// <returns>Configured renderer for the tag</returns>
        private ISvgNodeRenderer CreateRenderer(IElementNode tag, ISvgNodeRenderer parent) {
            return rendererFactory.CreateSvgNodeRendererForTag(tag, parent);
        }

        /// <summary>Check if this node is a text node that needs to be processed by the parent</summary>
        /// <param name="node">node to check</param>
        /// <returns>true if the node should be processed as text, false otherwise</returns>
        private bool ProcessAsText(INode node) {
            return node is ITextNode;
        }

        /// <summary>Process the text contained in the text-node</summary>
        /// <param name="textNode">node containing text to process</param>
        private void ProcessText(ITextNode textNode) {
            ISvgNodeRenderer parentRenderer = this.processorState.Top();
            if (parentRenderer != null && parentRenderer is TextSvgNodeRenderer) {
                // when svg is parsed by jsoup it leaves all whitespace in text element as is. Meaning that
                // tab/space indented xml files will retain their tabs and spaces.
                // The following regex replaces all whitespace with a single space.
                //TODO(RND-906) evaluate regex and trim methods
                String trimmedText = iText.IO.Util.StringUtil.ReplaceAll(textNode.WholeText(), "\\s+", " ");
                //Trim leading whitespace
                trimmedText = SvgTextUtil.TrimLeadingWhitespace(trimmedText);
                //Trim trailing whitespace
                trimmedText = SvgTextUtil.TrimTrailingWhitespace(trimmedText);
                parentRenderer.SetAttribute(SvgConstants.Attributes.TEXT_CONTENT, trimmedText);
            }
        }

        /// <summary>Find the first element in the node-tree that corresponds with the passed tag-name.</summary>
        /// <remarks>Find the first element in the node-tree that corresponds with the passed tag-name. Search is performed depth-first
        ///     </remarks>
        /// <param name="node">root-node to start with</param>
        /// <param name="tagName">name of the tag that needs to be fonund</param>
        /// <returns>IElementNode</returns>
        internal virtual IElementNode FindFirstElement(INode node, String tagName) {
            LinkedList<INode> q = new LinkedList<INode>();
            q.Add(node);
            while (!q.IsEmpty()) {
                INode currentNode = q.JGetFirst();
                q.RemoveFirst();
                if (currentNode == null) {
                    return null;
                }
                if (currentNode is IElementNode && ((IElementNode)currentNode).Name() != null && ((IElementNode)currentNode
                    ).Name().Equals(tagName)) {
                    return (IElementNode)currentNode;
                }
                foreach (INode child in currentNode.ChildNodes()) {
                    if (child is IElementNode) {
                        q.Add(child);
                    }
                }
            }
            return null;
        }
    }
}
