#!/usr/bin/env python3
"""Generate the portfolio-friendly development report PDF from its Markdown source."""

from __future__ import annotations

import html
import re
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    KeepTogether,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "development" / "mock_vertical_slice_development_report.md"
OUTPUT = ROOT / "output" / "pdf" / "mock_vertical_slice_development_report.pdf"
FONT_PATH = Path("/System/Library/Fonts/Supplemental/AppleGothic.ttf")

INK = colors.HexColor("#172033")
MUTED = colors.HexColor("#596579")
ACCENT = colors.HexColor("#496B9E")
ACCENT_LIGHT = colors.HexColor("#EAF0F8")
LINE = colors.HexColor("#D7DEE9")
CODE_BG = colors.HexColor("#F4F6F9")


def register_fonts() -> None:
    if not FONT_PATH.exists():
        raise FileNotFoundError(f"Korean font not found: {FONT_PATH}")
    pdfmetrics.registerFont(TTFont("Korean", str(FONT_PATH)))
    pdfmetrics.registerFontFamily("Korean", normal="Korean", bold="Korean", italic="Korean", boldItalic="Korean")


def inline_markup(text: str) -> str:
    escaped = html.escape(text.strip())
    escaped = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", escaped)
    escaped = re.sub(r"`(.+?)`", r'<font color="#345A8A">\1</font>', escaped)
    return escaped


def build_styles() -> dict[str, ParagraphStyle]:
    base = getSampleStyleSheet()
    return {
        "title": ParagraphStyle(
            "TitleK", parent=base["Title"], fontName="Korean", fontSize=26,
            leading=34, textColor=INK, alignment=TA_CENTER, spaceAfter=12,
        ),
        "subtitle": ParagraphStyle(
            "SubtitleK", parent=base["Normal"], fontName="Korean", fontSize=10,
            leading=16, textColor=MUTED, alignment=TA_CENTER, spaceAfter=8,
        ),
        "h2": ParagraphStyle(
            "H2K", parent=base["Heading2"], fontName="Korean", fontSize=17,
            leading=23, textColor=ACCENT, spaceBefore=15, spaceAfter=8, keepWithNext=True,
        ),
        "h3": ParagraphStyle(
            "H3K", parent=base["Heading3"], fontName="Korean", fontSize=12.5,
            leading=18, textColor=INK, spaceBefore=10, spaceAfter=5, keepWithNext=True,
        ),
        "body": ParagraphStyle(
            "BodyK", parent=base["BodyText"], fontName="Korean", fontSize=9.5,
            leading=15.5, textColor=INK, alignment=TA_LEFT, spaceAfter=7,
            wordWrap="CJK",
        ),
        "bullet": ParagraphStyle(
            "BulletK", parent=base["BodyText"], fontName="Korean", fontSize=9.3,
            leading=15, textColor=INK, leftIndent=14, firstLineIndent=-8,
            spaceAfter=3, wordWrap="CJK",
        ),
        "code": ParagraphStyle(
            "CodeK", parent=base["Code"], fontName="Korean", fontSize=8.2,
            leading=13, textColor=colors.HexColor("#263348"), wordWrap="CJK",
        ),
        "table": ParagraphStyle(
            "TableK", parent=base["BodyText"], fontName="Korean", fontSize=7.8,
            leading=11.5, textColor=INK, wordWrap="CJK",
        ),
        "table_header": ParagraphStyle(
            "TableHeaderK", parent=base["BodyText"], fontName="Korean", fontSize=8,
            leading=11.5, textColor=colors.white, alignment=TA_CENTER, wordWrap="CJK",
        ),
    }


def make_table(rows: list[list[str]], styles: dict[str, ParagraphStyle]) -> Table:
    column_count = max(len(row) for row in rows)
    normalized = [row + [""] * (column_count - len(row)) for row in rows]
    data = []
    for row_index, row in enumerate(normalized):
        style = styles["table_header"] if row_index == 0 else styles["table"]
        data.append([Paragraph(inline_markup(cell), style) for cell in row])
    widths = [174 * mm / column_count] * column_count
    table = Table(data, colWidths=widths, repeatRows=1, hAlign="LEFT")
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), ACCENT),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("GRID", (0, 0), (-1, -1), 0.45, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 6),
        ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F8FAFC")]),
    ]))
    return table


def parse_markdown(markdown: str, styles: dict[str, ParagraphStyle]) -> list:
    lines = markdown.splitlines()
    story: list = []
    index = 0
    first_heading = True
    paragraph_buffer: list[str] = []

    def flush_paragraph() -> None:
        if paragraph_buffer:
            story.append(Paragraph(inline_markup(" ".join(paragraph_buffer)), styles["body"]))
            paragraph_buffer.clear()

    while index < len(lines):
        line = lines[index]
        stripped = line.strip()

        if stripped.startswith("```"):
            flush_paragraph()
            code_lines: list[str] = []
            index += 1
            while index < len(lines) and not lines[index].strip().startswith("```"):
                code_lines.append(html.escape(lines[index]))
                index += 1
            code_text = "<br/>".join(code_lines) or " "
            code_box = Table([[Paragraph(code_text, styles["code"]) ]], colWidths=[174 * mm])
            code_box.setStyle(TableStyle([
                ("BACKGROUND", (0, 0), (-1, -1), CODE_BG),
                ("BOX", (0, 0), (-1, -1), 0.6, LINE),
                ("LEFTPADDING", (0, 0), (-1, -1), 10),
                ("RIGHTPADDING", (0, 0), (-1, -1), 10),
                ("TOPPADDING", (0, 0), (-1, -1), 8),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
            ]))
            story.extend([code_box, Spacer(1, 7)])
        elif stripped.startswith("|") and stripped.endswith("|"):
            flush_paragraph()
            table_lines: list[str] = []
            while index < len(lines) and lines[index].strip().startswith("|"):
                table_lines.append(lines[index].strip())
                index += 1
            rows = [[cell.strip() for cell in row.strip("|").split("|")] for row in table_lines]
            if len(rows) > 1 and all(set(cell) <= {"-", ":"} for cell in rows[1]):
                rows.pop(1)
            story.extend([make_table(rows, styles), Spacer(1, 8)])
            continue
        elif stripped.startswith("# "):
            flush_paragraph()
            if not first_heading:
                story.append(PageBreak())
            story.extend([
                Spacer(1, 20 * mm),
                Paragraph(inline_markup(stripped[2:]), styles["title"]),
                Table([[""]], colWidths=[45 * mm], rowHeights=[1.2], style=TableStyle([("BACKGROUND", (0, 0), (-1, -1), ACCENT)])),
                Spacer(1, 8 * mm),
            ])
            first_heading = False
        elif stripped.startswith("## "):
            flush_paragraph()
            story.append(Paragraph(inline_markup(stripped[3:]), styles["h2"]))
        elif stripped.startswith("### "):
            flush_paragraph()
            story.append(Paragraph(inline_markup(stripped[4:]), styles["h3"]))
        elif stripped.startswith("- "):
            flush_paragraph()
            story.append(Paragraph("• " + inline_markup(stripped[2:]), styles["bullet"]))
        elif re.match(r"^\d+\.\s", stripped):
            flush_paragraph()
            story.append(Paragraph(inline_markup(stripped), styles["bullet"]))
        elif stripped.startswith("**") and ":**" in stripped:
            flush_paragraph()
            story.append(Paragraph(inline_markup(stripped), styles["subtitle"]))
        elif not stripped:
            flush_paragraph()
        else:
            paragraph_buffer.append(stripped)
        index += 1

    flush_paragraph()
    while story and isinstance(story[-1], Spacer):
        story.pop()
    return story


def draw_page(canvas, document) -> None:
    canvas.saveState()
    page = canvas.getPageNumber()
    canvas.setStrokeColor(LINE)
    canvas.line(20 * mm, 15 * mm, A4[0] - 20 * mm, 15 * mm)
    canvas.setFillColor(MUTED)
    canvas.setFont("Korean", 7.5)
    canvas.drawString(20 * mm, 10 * mm, "빙의했더니 영지 멸망 3년 전 · 개발 기록")
    canvas.drawRightString(A4[0] - 20 * mm, 10 * mm, f"{page}")
    canvas.restoreState()


def main() -> None:
    register_fonts()
    styles = build_styles()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    markdown = SOURCE.read_text(encoding="utf-8")
    document = SimpleDocTemplate(
        str(OUTPUT), pagesize=A4,
        leftMargin=18 * mm, rightMargin=18 * mm,
        topMargin=17 * mm, bottomMargin=21 * mm,
        title="Mock 세로 슬라이스 개발 기록",
        author="빙의했더니 영지 멸망 3년 전 프로젝트",
        subject="Godot, ASP.NET Core, SQLite 기반 Mock 세로 슬라이스 개발 과정",
    )
    document.build(parse_markdown(markdown, styles), onFirstPage=draw_page, onLaterPages=draw_page)
    print(OUTPUT)


if __name__ == "__main__":
    main()
